using System.IO.Abstractions.TestingHelpers;
using System.Collections.ObjectModel;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Octans.Core.Http;
using Octans.Core.Http.Bandwidth;
using Octans.Core.Http.Models;
using Octans.Data.Models;
using Polly.CircuitBreaker;

namespace Octans.Tests.Downloads;

public class HttpDownloaderTests
{
    private readonly IDownloadBandwidthGate _bandwidthGate = Substitute.For<IDownloadBandwidthGate>();
    private readonly FakeDownloadDiskSpaceGuard _diskSpaceGuard = new();
    private readonly IDownloadStateService _stateService = Substitute.For<IDownloadStateService>();
    private readonly IDownloadLifecycleService _lifecycle = Substitute.For<IDownloadLifecycleService>();
    private readonly IActiveDownloadRegistry _activeDownloads = Substitute.For<IActiveDownloadRegistry>();
    private readonly IDownloadHostCircuitRegistry _hostCircuitRegistry = Substitute.For<IDownloadHostCircuitRegistry>();
    private readonly MockFileSystem _fileSystem = new();
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly DownloadTelemetry _telemetry = new();
    private readonly HttpDownloader _sut;
    private readonly CancellationTokenSource _cts = new();
    private readonly TestHttpMessageHandler _messageHandler = new();

    public HttpDownloaderTests()
    {
        _sut = CreateDownloader();

        // Setup download token
        _activeDownloads
            .GetToken(Arg.Any<Guid>())
            .Returns(CancellationToken.None);

        _lifecycle
            .MarkInProgressAsync(Arg.Any<Guid>())
            .Returns(true);
    }

    private HttpDownloader CreateDownloader(DownloadManagerOptions? options = null)
    {
        var factory = Substitute.For<IHttpClientFactory>();

        var httpClient = new HttpClient(_messageHandler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        factory.CreateClient("DownloadClient").Returns(httpClient);

        return new(
            _bandwidthGate,
            _diskSpaceGuard,
            _stateService,
            _lifecycle,
            _activeDownloads,
            _hostCircuitRegistry,
            factory,
            new DownloadRequestHeaderProvider(Options.Create(options ?? new DownloadManagerOptions())),
            _fileSystem,
            new DownloadStagingPaths(_fileSystem),
            _timeProvider,
            Options.Create(options ?? new DownloadManagerOptions()),
            _telemetry,
            NullLogger<HttpDownloader>.Instance);
    }

    [Fact]
    public async Task ProcessDownloadAsync_WritesToStagingThenMovesToDestinationOnCompletion()
    {
        var downloadId = Guid.NewGuid();
        var destinationPath = "/downloads/test.txt";
        var download = CreateDownload(downloadId, destinationPath);
        var content = new PausingReadContent("hello");
        _messageHandler.ResponseToReturn = new(HttpStatusCode.OK)
        {
            Content = content
        };

        var downloadTask = _sut.ProcessDownloadAsync(download, _cts.Token);

        await content.SecondReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var stagingPath = GetStagingPath(downloadId, destinationPath);
        Assert.True(_fileSystem.File.Exists(stagingPath));
        Assert.False(_fileSystem.File.Exists(destinationPath));

        content.ReleaseSecondRead();
        await downloadTask;

        Assert.False(_fileSystem.File.Exists(stagingPath));
        Assert.Equal("hello", await _fileSystem.File.ReadAllTextAsync(destinationPath));
        await _lifecycle.Received(1).MarkCompletedAsync(
            downloadId,
            Arg.Is<DownloadTerminalUpdate>(update =>
                update.Outcome == DownloadTerminalOutcome.Completed &&
                update.HttpStatusCode == 200));
        await _bandwidthGate.Received(2).WaitForBytesAsync("example.com", Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessDownloadAsync_HandlesHttpFailure_UpdatesStateToFailed()
    {
        // Setup
        var downloadId = Guid.NewGuid();
        var destinationPath = "/downloads/test.txt";
        var url = "https://example.com/test.txt";

        var download = new QueuedDownload
        {
            Id = downloadId,
            Url = url,
            DestinationPath = destinationPath,
            Domain = "example.com"
        };

        // Configure HTTP response to fail
        _messageHandler.ResponseToReturn = new(HttpStatusCode.NotFound);

        // Act
        await _sut.ProcessDownloadAsync(download, _cts.Token);

        // Assert
        // Verify state updates
        await _lifecycle.Received(1).MarkInProgressAsync(downloadId);
        await _lifecycle.Received(1).MarkFailedAsync(
            downloadId,
            Arg.Is<string>(s => s.Contains("404")),
            DownloadFailureCategory.Http,
            DownloadTerminalOutcome.TerminalHttpFailure,
            404);

        // Verify file was not created
        Assert.False(_fileSystem.File.Exists(destinationPath));
        Assert.False(_fileSystem.File.Exists(GetStagingPath(downloadId, destinationPath)));
    }

    [Fact]
    public async Task ProcessDownloadAsync_WhenHostCircuitIsOpen_MarksDownloadFailedWithClearMessage()
    {
        var downloadId = Guid.NewGuid();
        var destinationPath = "/downloads/test.txt";
        var download = new QueuedDownload
        {
            Id = downloadId,
            Url = "https://example.com/test.txt",
            DestinationPath = destinationPath,
            Domain = "example.com"
        };
        _messageHandler.ExceptionToThrow = new BrokenCircuitException("Circuit is open.");

        await _sut.ProcessDownloadAsync(download, _cts.Token);

        await _lifecycle.Received(1).MarkFailedAsync(
            downloadId,
            Arg.Is<string>(message => message.Contains("Host circuit for example.com is open")),
            DownloadFailureCategory.Network);
        Assert.False(_fileSystem.File.Exists(destinationPath));
        Assert.False(_fileSystem.File.Exists(GetStagingPath(downloadId, destinationPath)));
    }

    [Fact]
    public async Task ProcessDownloadAsync_WhenStartIsRejected_DoesNotStartHttpRequest()
    {
        var downloadId = Guid.NewGuid();
        var download = new QueuedDownload
        {
            Id = downloadId,
            Url = "https://example.com/test.txt",
            DestinationPath = "/downloads/test.txt",
            Domain = "example.com"
        };

        _lifecycle.MarkInProgressAsync(downloadId).Returns(false);

        await _sut.ProcessDownloadAsync(download, _cts.Token);

        await _lifecycle.Received(1).MarkInProgressAsync(downloadId);
        await _lifecycle.DidNotReceive().MarkCompletedAsync(downloadId);
        await _lifecycle.DidNotReceive().MarkFailedAsync(downloadId, Arg.Any<string>());
        Assert.False(_messageHandler.RequestStarted.Task.IsCompleted);
    }

    [Fact]
    public async Task ProcessDownloadAsync_HandlesCancellationAfterPartialWrite_DeletesStagingAndUpdatesStateToCanceled()
    {
        var downloadId = Guid.NewGuid();
        var destinationPath = "/downloads/test.txt";
        var url = "https://example.com/test.txt";

        var download = new QueuedDownload
        {
            Id = downloadId,
            Url = url,
            DestinationPath = destinationPath,
            Domain = "example.com"
        };

        using var downloadCts = new CancellationTokenSource();
        _activeDownloads.GetToken(downloadId).Returns(downloadCts.Token);

        var content = new PausingReadContent("hello");
        _messageHandler.ResponseToReturn = new(HttpStatusCode.OK)
        {
            Content = content
        };

        var downloadTask = _sut.ProcessDownloadAsync(download, _cts.Token);

        await content.SecondReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await downloadCts.CancelAsync();

        await downloadTask;

        await _lifecycle.Received(1).MarkInProgressAsync(downloadId);
        await _lifecycle.Received(1).MarkCanceledAsync(downloadId);
        await _lifecycle.DidNotReceive().MarkCompletedAsync(downloadId);
        Assert.False(_fileSystem.File.Exists(destinationPath));
        Assert.False(_fileSystem.File.Exists(GetStagingPath(downloadId, destinationPath)));
    }

    [Fact]
    public async Task ProcessDownloadAsync_WhenBodyReadFailsAfterPartialWrite_DeletesStagingAndDoesNotCreateFinalFile()
    {
        var downloadId = Guid.NewGuid();
        var destinationPath = "/downloads/test.txt";
        var download = CreateDownload(downloadId, destinationPath);
        var content = new PausingReadContent("hello")
        {
            ThrowOnSecondRead = true
        };
        _messageHandler.ResponseToReturn = new(HttpStatusCode.OK)
        {
            Content = content
        };

        await _sut.ProcessDownloadAsync(download, _cts.Token);

        await _lifecycle.Received(1).MarkFailedAsync(
            downloadId,
            Arg.Is<string>(s => s.Contains("stream failed")),
            DownloadFailureCategory.Filesystem);
        await _lifecycle.DidNotReceive().MarkCompletedAsync(downloadId);
        Assert.False(_fileSystem.File.Exists(destinationPath));
        Assert.False(_fileSystem.File.Exists(GetStagingPath(downloadId, destinationPath)));
    }

    [Fact]
    public async Task ProcessDownloadAsync_WhenContentLengthDoesNotMatch_DeletesStagingAndDoesNotCreateFinalFile()
    {
        var downloadId = Guid.NewGuid();
        var destinationPath = "/downloads/test.txt";
        var download = CreateDownload(downloadId, destinationPath);
        var content = new StreamContent(new MemoryStream("short"u8.ToArray()));
        content.Headers.ContentLength = 10;
        _messageHandler.ResponseToReturn = new(HttpStatusCode.OK)
        {
            Content = content
        };

        await _sut.ProcessDownloadAsync(download, _cts.Token);

        await _lifecycle.Received(1).MarkFailedAsync(
            downloadId,
            Arg.Is<string>(s => s.Contains("server reported 10 bytes")),
            DownloadFailureCategory.Validation,
            DownloadTerminalOutcome.ValidationFailed,
            validationMessage: Arg.Is<string>(s => s.Contains("server reported 10 bytes")));
        await _lifecycle.DidNotReceive().MarkCompletedAsync(downloadId);
        Assert.False(_fileSystem.File.Exists(destinationPath));
        Assert.False(_fileSystem.File.Exists(GetStagingPath(downloadId, destinationPath)));
    }

    [Fact]
    public async Task ProcessDownloadAsync_WhenExpectedHashMatches_CompletesAndPersistsResponseMetadata()
    {
        var downloadId = Guid.NewGuid();
        var destinationPath = "/downloads/test.txt";
        var download = CreateDownload(downloadId, destinationPath);
        download.ExpectedHashes = "[{\"algorithm\":\"SHA-256\",\"value\":\"2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824\"}]";
        var lastModified = new DateTimeOffset(2026, 5, 15, 12, 34, 56, TimeSpan.Zero);
        var content = new StringContent("hello");
        content.Headers.LastModified = lastModified;
        _messageHandler.ResponseToReturn = new(HttpStatusCode.OK)
        {
            Content = content,
            Headers =
            {
                ETag = new("\"abc123\"")
            }
        };

        await _sut.ProcessDownloadAsync(download, _cts.Token);

        Assert.Equal("hello", await _fileSystem.File.ReadAllTextAsync(destinationPath));
        await _lifecycle.Received(1).MarkCompletedAsync(
            downloadId,
            Arg.Is<DownloadTerminalUpdate>(update =>
                update.Outcome == DownloadTerminalOutcome.Completed &&
                update.ResponseETag == "\"abc123\"" &&
                update.ResponseLastModified == lastModified));
        await _lifecycle.DidNotReceive().MarkFailedAsync(downloadId, Arg.Any<string>());
        Assert.False(_fileSystem.File.Exists(GetStagingPath(downloadId, destinationPath)));
    }

    [Fact]
    public async Task ProcessDownloadAsync_WhenExpectedHashDoesNotMatch_DeletesStagingAndDoesNotCreateFinalFile()
    {
        var downloadId = Guid.NewGuid();
        var destinationPath = "/downloads/test.txt";
        var download = CreateDownload(downloadId, destinationPath);
        download.ExpectedHashes = "[{\"algorithm\":\"SHA-256\",\"value\":\"0000000000000000000000000000000000000000000000000000000000000000\"}]";
        _messageHandler.ResponseToReturn = new(HttpStatusCode.OK)
        {
            Content = new StringContent("hello")
        };

        await _sut.ProcessDownloadAsync(download, _cts.Token);

        await _lifecycle.Received(1).MarkFailedAsync(
            downloadId,
            Arg.Is<string>(s => s.Contains("Hash mismatch")),
            DownloadFailureCategory.Validation,
            DownloadTerminalOutcome.ValidationFailed,
            validationMessage: Arg.Is<string>(s => s.Contains("Hash mismatch")));
        await _lifecycle.DidNotReceive().MarkCompletedAsync(downloadId, Arg.Any<DownloadTerminalUpdate>());
        Assert.False(_fileSystem.File.Exists(destinationPath));
        Assert.False(_fileSystem.File.Exists(GetStagingPath(downloadId, destinationPath)));
    }

    [Fact]
    public async Task ProcessDownloadAsync_WhenHashValidatorIsUnsupported_FailsBeforeStreaming()
    {
        var downloadId = Guid.NewGuid();
        var destinationPath = "/downloads/test.txt";
        var download = CreateDownload(downloadId, destinationPath);
        download.ExpectedHashes = "[{\"algorithm\":\"crc32\",\"value\":\"12345678\"}]";
        _messageHandler.ResponseToReturn = new(HttpStatusCode.OK)
        {
            Content = new StringContent("hello")
        };

        await _sut.ProcessDownloadAsync(download, _cts.Token);

        await _lifecycle.Received(1).MarkFailedAsync(
            downloadId,
            Arg.Is<string>(s => s.Contains("Unsupported hash validator")),
            DownloadFailureCategory.Validation,
            DownloadTerminalOutcome.ValidationFailed,
            validationMessage: Arg.Is<string>(s => s.Contains("Unsupported hash validator")));
        await _lifecycle.DidNotReceive().MarkCompletedAsync(downloadId, Arg.Any<DownloadTerminalUpdate>());
        await _bandwidthGate.DidNotReceive().WaitForBytesAsync(
            Arg.Any<string>(),
            Arg.Any<long>(),
            Arg.Any<CancellationToken>());
        Assert.False(_fileSystem.File.Exists(destinationPath));
        Assert.False(_fileSystem.File.Exists(GetStagingPath(downloadId, destinationPath)));
    }

    [Fact]
    public async Task ProcessDownloadAsync_WhenKnownContentLengthExceedsAvailableSpace_FailsBeforeWritingStagingFile()
    {
        var downloadId = Guid.NewGuid();
        var destinationPath = "/downloads/test.txt";
        var download = CreateDownload(downloadId, destinationPath);
        var content = new ByteArrayContent(new byte[10]);
        _messageHandler.ResponseToReturn = new(HttpStatusCode.OK)
        {
            Content = content
        };

        _diskSpaceGuard.FailWhenBytesNeededAtLeast = 10;

        await _sut.ProcessDownloadAsync(download, _cts.Token);

        await _lifecycle.Received(1).MarkFailedAsync(
            downloadId,
            Arg.Is<string>(s => s.Contains("Insufficient free space")),
            DownloadFailureCategory.Filesystem);
        await _lifecycle.DidNotReceive().MarkCompletedAsync(downloadId, Arg.Any<DownloadTerminalUpdate>());
        await _bandwidthGate.DidNotReceive().WaitForBytesAsync(
            Arg.Any<string>(),
            Arg.Any<long>(),
            Arg.Any<CancellationToken>());
        Assert.False(_fileSystem.File.Exists(destinationPath));
        Assert.False(_fileSystem.File.Exists(GetStagingPath(downloadId, destinationPath)));
    }

    [Fact]
    public async Task ProcessDownloadAsync_WhenKnownContentLengthExceedsSizeLimit_FailsBeforeStreaming()
    {
        var sut = CreateDownloader(new()
        {
            SizeLimits = new()
            {
                MaxBytes = 5
            }
        });
        var downloadId = Guid.NewGuid();
        var destinationPath = "/downloads/test.txt";
        var download = CreateDownload(downloadId, destinationPath);
        _messageHandler.ResponseToReturn = new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[10])
        };

        await sut.ProcessDownloadAsync(download, _cts.Token);

        await _lifecycle.Received(1).MarkFailedAsync(
            downloadId,
            Arg.Is<string>(s => s.Contains("reported 10 bytes") && s.Contains("max download size of 5 bytes")),
            DownloadFailureCategory.SizeLimit,
            DownloadTerminalOutcome.ValidationFailed,
            validationMessage: Arg.Is<string>(s =>
                s.Contains("reported 10 bytes") && s.Contains("max download size of 5 bytes")));
        await _lifecycle.DidNotReceive().MarkCompletedAsync(downloadId, Arg.Any<DownloadTerminalUpdate>());
        await _bandwidthGate.DidNotReceive().WaitForBytesAsync(
            Arg.Any<string>(),
            Arg.Any<long>(),
            Arg.Any<CancellationToken>());
        Assert.Equal(0, _diskSpaceGuard.CheckCount);
        Assert.False(_fileSystem.File.Exists(destinationPath));
        Assert.False(_fileSystem.File.Exists(GetStagingPath(downloadId, destinationPath)));
    }

    [Fact]
    public async Task ProcessDownloadAsync_WhenUnknownLengthResponseExceedsSizeLimit_FailsDuringStreaming()
    {
        var sut = CreateDownloader(new()
        {
            SizeLimits = new()
            {
                MaxBytes = 5
            }
        });
        var downloadId = Guid.NewGuid();
        var destinationPath = "/downloads/test.txt";
        var download = CreateDownload(downloadId, destinationPath);
        _messageHandler.ResponseToReturn = new(HttpStatusCode.OK)
        {
            Content = new UnknownLengthContent(new byte[10])
        };

        await sut.ProcessDownloadAsync(download, _cts.Token);

        await _lifecycle.Received(1).MarkFailedAsync(
            downloadId,
            Arg.Is<string>(s => s.Contains("exceeded") && s.Contains("max download size of 5 bytes")),
            DownloadFailureCategory.SizeLimit,
            DownloadTerminalOutcome.ValidationFailed,
            validationMessage: Arg.Is<string>(s =>
                s.Contains("exceeded") && s.Contains("max download size of 5 bytes")));
        await _lifecycle.DidNotReceive().MarkCompletedAsync(downloadId, Arg.Any<DownloadTerminalUpdate>());
        Assert.False(_fileSystem.File.Exists(destinationPath));
        Assert.False(_fileSystem.File.Exists(GetStagingPath(downloadId, destinationPath)));
    }

    [Fact]
    public async Task ProcessDownloadAsync_WhenContentLengthLiesBelowSizeLimit_FailsWhenStreamCrossesLimit()
    {
        var sut = CreateDownloader(new()
        {
            SizeLimits = new()
            {
                MaxBytes = 5
            }
        });
        var downloadId = Guid.NewGuid();
        var destinationPath = "/downloads/test.txt";
        var download = CreateDownload(downloadId, destinationPath);
        var content = new ByteArrayContent(new byte[10]);
        content.Headers.ContentLength = 4;
        _messageHandler.ResponseToReturn = new(HttpStatusCode.OK)
        {
            Content = content
        };

        await sut.ProcessDownloadAsync(download, _cts.Token);

        await _lifecycle.Received(1).MarkFailedAsync(
            downloadId,
            Arg.Is<string>(s => s.Contains("exceeded") && s.Contains("max download size of 5 bytes")),
            DownloadFailureCategory.SizeLimit,
            DownloadTerminalOutcome.ValidationFailed,
            validationMessage: Arg.Is<string>(s =>
                s.Contains("exceeded") && s.Contains("max download size of 5 bytes")));
        await _lifecycle.DidNotReceive().MarkCompletedAsync(downloadId, Arg.Any<DownloadTerminalUpdate>());
        Assert.False(_fileSystem.File.Exists(destinationPath));
        Assert.False(_fileSystem.File.Exists(GetStagingPath(downloadId, destinationPath)));
    }

    [Fact]
    public async Task ProcessDownloadAsync_WhenDiskSpaceRunsOutDuringStreaming_DeletesStagingAndDoesNotCreateFinalFile()
    {
        var downloadId = Guid.NewGuid();
        var destinationPath = "/downloads/test.txt";
        var download = CreateDownload(downloadId, destinationPath);
        var content = new PausingReadContent("hello");
        _messageHandler.ResponseToReturn = new(HttpStatusCode.OK)
        {
            Content = content
        };

        var downloadTask = _sut.ProcessDownloadAsync(download, _cts.Token);

        await content.SecondReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(_fileSystem.File.Exists(GetStagingPath(downloadId, destinationPath)));

        _diskSpaceGuard.FailNextCheck = true;
        content.ReleaseSecondRead();
        await downloadTask;

        await _lifecycle.Received(1).MarkFailedAsync(
            downloadId,
            Arg.Is<string>(s => s.Contains("Insufficient free space")),
            DownloadFailureCategory.Filesystem);
        await _lifecycle.DidNotReceive().MarkCompletedAsync(downloadId, Arg.Any<DownloadTerminalUpdate>());
        Assert.False(_fileSystem.File.Exists(destinationPath));
        Assert.False(_fileSystem.File.Exists(GetStagingPath(downloadId, destinationPath)));
    }

    [Fact]
    public async Task ProcessDownloadAsync_WhenAllowedContentTypeMatches_CompletesDownload()
    {
        var downloadId = Guid.NewGuid();
        var destinationPath = "/downloads/test.bin";
        var download = CreateDownload(downloadId, destinationPath);
        download.AllowedContentTypes = """["image/*"]""";
        var content = new StringContent("image bytes");
        content.Headers.ContentType = new("image/jpeg");
        _messageHandler.ResponseToReturn = new(HttpStatusCode.OK)
        {
            Content = content
        };

        await _sut.ProcessDownloadAsync(download, _cts.Token);

        Assert.Equal("image bytes", await _fileSystem.File.ReadAllTextAsync(destinationPath));
        await _lifecycle.Received(1).MarkCompletedAsync(
            downloadId,
            Arg.Is<DownloadTerminalUpdate>(update => update.ResponseContentType == "image/jpeg"));
    }

    [Fact]
    public async Task ProcessDownloadAsync_WhenAllowedContentTypeMismatches_FailsBeforeStreaming()
    {
        var downloadId = Guid.NewGuid();
        var destinationPath = "/downloads/test.jpg";
        var download = CreateDownload(downloadId, destinationPath);
        var content = new PausingReadContent("<html>not an image</html>");
        content.Headers.ContentType = new("text/html");
        _messageHandler.ResponseToReturn = new(HttpStatusCode.OK)
        {
            Content = content
        };

        await _sut.ProcessDownloadAsync(download, _cts.Token);

        await _lifecycle.Received(1).MarkFailedAsync(
            downloadId,
            Arg.Is<string>(s => s.Contains("text/html") && s.Contains("image/*")),
            DownloadFailureCategory.Validation,
            DownloadTerminalOutcome.ValidationFailed,
            validationMessage: Arg.Is<string>(s => s.Contains("text/html") && s.Contains("image/*")),
            responseContentType: "text/html");
        await _lifecycle.DidNotReceive().MarkCompletedAsync(downloadId);
        await _bandwidthGate.DidNotReceive().WaitForBytesAsync(
            Arg.Any<string>(),
            Arg.Any<long>(),
            Arg.Any<CancellationToken>());
        Assert.False(content.SecondReadStarted.Task.IsCompleted);
        Assert.False(_fileSystem.File.Exists(destinationPath));
        Assert.False(_fileSystem.File.Exists(GetStagingPath(downloadId, destinationPath)));
    }

    [Fact]
    public async Task ProcessDownloadAsync_WhenContentTypeIsMissing_AllowsByDefault()
    {
        var downloadId = Guid.NewGuid();
        var destinationPath = "/downloads/test.jpg";
        var download = CreateDownload(downloadId, destinationPath);
        _messageHandler.ResponseToReturn = new(HttpStatusCode.OK)
        {
            Content = new StringContent("bytes")
        };
        _messageHandler.ResponseToReturn.Content.Headers.ContentType = null;

        await _sut.ProcessDownloadAsync(download, _cts.Token);

        Assert.Equal("bytes", await _fileSystem.File.ReadAllTextAsync(destinationPath));
        await _lifecycle.Received(1).MarkCompletedAsync(
            downloadId,
            Arg.Is<DownloadTerminalUpdate>(update => update.Outcome == DownloadTerminalOutcome.Completed));
    }

    [Fact]
    public async Task ProcessDownloadAsync_WhenMissingContentTypeIsDisallowed_FailsValidation()
    {
        var sut = CreateDownloader(new()
        {
            ContentTypeValidation = new()
            {
                AllowMissingContentType = false
            }
        });
        var downloadId = Guid.NewGuid();
        var destinationPath = "/downloads/test.jpg";
        var download = CreateDownload(downloadId, destinationPath);
        _messageHandler.ResponseToReturn = new(HttpStatusCode.OK)
        {
            Content = new StringContent("bytes")
        };
        _messageHandler.ResponseToReturn.Content.Headers.ContentType = null;

        await sut.ProcessDownloadAsync(download, _cts.Token);

        await _lifecycle.Received(1).MarkFailedAsync(
            downloadId,
            Arg.Is<string>(s => s.Contains("missing") && s.Contains("image/*")),
            DownloadFailureCategory.Validation,
            DownloadTerminalOutcome.ValidationFailed,
            validationMessage: Arg.Is<string>(s => s.Contains("missing") && s.Contains("image/*")));
        Assert.False(_fileSystem.File.Exists(destinationPath));
    }

    [Fact]
    public async Task ProcessDownloadAsync_WhenContentTypeIsGeneric_AllowsByDefault()
    {
        var downloadId = Guid.NewGuid();
        var destinationPath = "/downloads/test.jpg";
        var download = CreateDownload(downloadId, destinationPath);
        var content = new StringContent("bytes");
        content.Headers.ContentType = new("application/octet-stream");
        _messageHandler.ResponseToReturn = new(HttpStatusCode.OK)
        {
            Content = content
        };

        await _sut.ProcessDownloadAsync(download, _cts.Token);

        Assert.Equal("bytes", await _fileSystem.File.ReadAllTextAsync(destinationPath));
        await _lifecycle.Received(1).MarkCompletedAsync(
            downloadId,
            Arg.Is<DownloadTerminalUpdate>(update => update.ResponseContentType == "application/octet-stream"));
    }


    [Fact]
    public async Task ProcessDownloadAsync_AppliesDomainSpecificHeaders()
    {
        var options = new DownloadManagerOptions();
        options.RequestHeaders.DefaultUserAgent = "Octans-Test/1.0";
        options.RequestHeaders.Domains["example.com"] = new()
        {
            UserAgent = "ExampleBot/2.0",
            Authorization = "Bearer secret",
            Cookie = "session=abc"
        };
        options.RequestHeaders.Domains["example.com"].Headers["X-Source"] = "configured";

        var sut = CreateDownloader(options);
        var downloadId = Guid.NewGuid();
        var destinationPath = "/downloads/test.txt";
        var download = CreateDownload(downloadId, destinationPath);
        _messageHandler.ResponseToReturn = new(HttpStatusCode.OK)
        {
            Content = new StringContent("hello")
        };

        await sut.ProcessDownloadAsync(download, _cts.Token);

        var request = Assert.Single(_messageHandler.Requests);
        Assert.Equal("ExampleBot/2.0", request.Headers.UserAgent.ToString());
        Assert.Equal("Bearer secret", Assert.Single(request.Headers.GetValues("Authorization")));
        Assert.Equal("session=abc", Assert.Single(request.Headers.GetValues("Cookie")));
        Assert.Equal("configured", Assert.Single(request.Headers.GetValues("X-Source")));
    }

    [Fact]
    public async Task ProcessDownloadAsync_WhenRequiredCredentialsAreMissing_FailsWithoutHttpRequest()
    {
        var options = new DownloadManagerOptions();
        options.RequestHeaders.Domains["example.com"] = new();
        options.RequestHeaders.Domains["example.com"].RequiredHeaders.Add("Authorization");
        var sut = CreateDownloader(options);
        var downloadId = Guid.NewGuid();
        var download = CreateDownload(downloadId, "/downloads/test.txt");

        await sut.ProcessDownloadAsync(download, _cts.Token);

        Assert.Empty(_messageHandler.Requests);
        await _lifecycle.Received(1).MarkFailedAsync(
            downloadId,
            Arg.Is<string>(message =>
                message.Contains("Authorization") &&
                !message.Contains("secret", StringComparison.OrdinalIgnoreCase)),
            DownloadFailureCategory.Authentication,
            DownloadTerminalOutcome.ValidationFailed,
            validationMessage: Arg.Is<string>(message => message.Contains("Authorization")));
    }

    private static QueuedDownload CreateDownload(Guid downloadId, string destinationPath)
    {
        return new()
        {
            Id = downloadId,
            Url = "https://example.com/test.txt",
            DestinationPath = destinationPath,
            Domain = "example.com"
        };
    }

    private string GetStagingPath(Guid downloadId, string destinationPath)
    {
        var destinationDirectory = _fileSystem.Path.GetDirectoryName(destinationPath) ??
                                   throw new InvalidOperationException();

        return _fileSystem.Path.Combine(destinationDirectory, ".octans-downloads", $"{downloadId}.part");
    }
}

public sealed class FakeDownloadDiskSpaceGuard : IDownloadDiskSpaceGuard
{
    public long? FailWhenBytesNeededAtLeast { get; set; }
    public bool FailNextCheck { get; set; }
    public int CheckCount { get; private set; }

    public void EnsureSufficientSpace(string destinationPath, long bytesNeeded)
    {
        CheckCount++;

        if (FailNextCheck)
        {
            FailNextCheck = false;
            throw new DownloadDiskSpaceException("Insufficient free space for download.");
        }

        if (FailWhenBytesNeededAtLeast is { } threshold && bytesNeeded >= threshold)
        {
            throw new DownloadDiskSpaceException("Insufficient free space for download.");
        }
    }
}

public class TestHttpMessageHandler : HttpMessageHandler
{
    public HttpResponseMessage? ResponseToReturn { get; set; }
    public Exception? ExceptionToThrow { get; set; }
    public TimeSpan DelayBeforeResponse { get; set; } = TimeSpan.Zero;
    public bool WaitForCancellation { get; set; }
    public TaskCompletionSource RequestStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Collection<HttpRequestMessage> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestStarted.TrySetResult();

        if (WaitForCancellation)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        if (DelayBeforeResponse > TimeSpan.Zero)
        {
            await Task.Delay(DelayBeforeResponse, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (ExceptionToThrow != null)
        {
            throw ExceptionToThrow;
        }

        return ResponseToReturn ?? new HttpResponseMessage(HttpStatusCode.OK);
    }
}

public sealed class PausingReadContent : StreamContent
{
    private readonly PausingReadStream _stream;

    public PausingReadContent(string body) : this(new PausingReadStream(body))
    {
    }

    private PausingReadContent(PausingReadStream stream) : base(stream)
    {
        _stream = stream;
    }

    public TaskCompletionSource SecondReadStarted => _stream.SecondReadStarted;
    public bool ThrowOnSecondRead
    {
        get => _stream.ThrowOnSecondRead;
        set => _stream.ThrowOnSecondRead = value;
    }

    public void ReleaseSecondRead()
    {
        _stream.ReleaseSecondRead();
    }
}

public sealed class PausingReadStream(string body) : Stream
{
    private readonly byte[] _bytes = System.Text.Encoding.UTF8.GetBytes(body);
    private readonly TaskCompletionSource _continueSecondRead =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _position;
    private bool _secondReadStarted;

    public TaskCompletionSource SecondReadStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool ThrowOnSecondRead { get; set; }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _bytes.Length;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position >= _bytes.Length)
        {
            return 0;
        }

        if (_position == 0)
        {
            var firstChunkLength = Math.Min(2, _bytes.Length);
            _bytes.AsMemory(0, firstChunkLength).CopyTo(buffer);
            _position += firstChunkLength;
            return firstChunkLength;
        }

        if (!_secondReadStarted)
        {
            _secondReadStarted = true;
            SecondReadStarted.TrySetResult();

            if (ThrowOnSecondRead)
            {
                throw new IOException("stream failed after partial body write");
            }

            await _continueSecondRead.Task.WaitAsync(cancellationToken);
        }

        var bytesRemaining = _bytes.Length - _position;
        var bytesToRead = Math.Min(buffer.Length, bytesRemaining);
        _bytes.AsMemory(_position, bytesToRead).CopyTo(buffer);
        _position += bytesToRead;

        return bytesToRead;
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public void ReleaseSecondRead()
    {
        _continueSecondRead.TrySetResult();
    }
}

public sealed class UnknownLengthContent(byte[] body) : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        return stream.WriteAsync(body).AsTask();
    }

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}
