using System.IO.Abstractions.TestingHelpers;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Octans.Core.Downloads;
using Octans.Core.Downloads.Bandwidth;
using Octans.Data.Models;

namespace Octans.Tests.Downloads;

public class HttpDownloaderTests
{
    private readonly IBandwidthLimiter _bandwidthLimiter = Substitute.For<IBandwidthLimiter>();
    private readonly IDownloadBandwidthGate _bandwidthGate = Substitute.For<IDownloadBandwidthGate>();
    private readonly IDownloadStateService _stateService = Substitute.For<IDownloadStateService>();
    private readonly IDownloadLifecycleService _lifecycle = Substitute.For<IDownloadLifecycleService>();
    private readonly IActiveDownloadRegistry _activeDownloads = Substitute.For<IActiveDownloadRegistry>();
    private readonly MockFileSystem _fileSystem = new();
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly HttpDownloader _sut;
    private readonly CancellationTokenSource _cts = new();
    private readonly TestHttpMessageHandler _messageHandler = new();

    public HttpDownloaderTests()
    {
        var factory = Substitute.For<IHttpClientFactory>();

        var httpClient = new HttpClient(_messageHandler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        factory.CreateClient("DownloadClient").Returns(httpClient);

        _sut = new(
            _bandwidthLimiter,
            _bandwidthGate,
            _stateService,
            _lifecycle,
            _activeDownloads,
            factory,
            _fileSystem,
            _timeProvider,
            NullLogger<HttpDownloader>.Instance);

        // Setup download token
        _activeDownloads
            .GetToken(Arg.Any<Guid>())
            .Returns(CancellationToken.None);

        _lifecycle
            .MarkInProgressAsync(Arg.Any<Guid>())
            .Returns(true);
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
        await _lifecycle.Received(1).MarkFailedAsync(downloadId, Arg.Is<string>(s => s.Contains("404")));

        // Verify file was not created
        Assert.False(_fileSystem.File.Exists(destinationPath));
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
    public async Task ProcessDownloadAsync_HandlesCancellation_UpdatesStateToCanceled()
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

        // Setup a cancellation token that will be triggered during download
        using var downloadCts = new CancellationTokenSource();
        _activeDownloads.GetToken(downloadId).Returns(downloadCts.Token);

        _messageHandler.WaitForCancellation = true;

        // Start the download
        var downloadTask = _sut.ProcessDownloadAsync(download, _cts.Token);

        await _messageHandler.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await downloadCts.CancelAsync();

        // Wait for the download to complete
        await downloadTask;

        // Assert
        // Verify state updates
        await _lifecycle.Received(1).MarkInProgressAsync(downloadId);
        await _lifecycle.Received(1).MarkCanceledAsync(downloadId);
        await _lifecycle.DidNotReceive().MarkCompletedAsync(downloadId);
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

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
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
