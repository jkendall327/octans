using System.IO.Abstractions.TestingHelpers;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Octans.Core.Downloads;
using Octans.Core.Downloaders;

namespace Octans.Tests.Downloads;

public class HttpDownloaderTests
{
    private readonly IBandwidthLimiter _bandwidthLimiter = Substitute.For<IBandwidthLimiter>();
    private readonly IDownloadStateService _stateService = Substitute.For<IDownloadStateService>();
    private readonly IDownloadService _downloadService = Substitute.For<IDownloadService>();
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
            _stateService,
            _downloadService,
            factory,
            _fileSystem,
            _timeProvider,
            NullLogger<HttpDownloader>.Instance);

        // Setup download token
        _downloadService
            .GetDownloadToken(Arg.Any<Guid>())
            .Returns(CancellationToken.None);
    }

    [Fact]
    public async Task ProcessDownloadAsync_CompletesSuccessfully_WhenHttpRequestSucceeds()
    {
        // Setup
        var downloadId = Guid.NewGuid();
        var testContent = "Test file content";
        var testBytes = System.Text.Encoding.UTF8.GetBytes(testContent);
        var destinationPath = "/downloads/test.txt";
        var url = "https://example.com/test.txt";

        var download = new QueuedDownload
        {
            Id = downloadId,
            Url = url,
            DestinationPath = destinationPath,
            Domain = "example.com"
        };

        // Configure HTTP response
        _messageHandler.ResponseToReturn = new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(testBytes)
        };
        _messageHandler.ResponseToReturn.Content.Headers.ContentLength = testBytes.Length;

        // Simulate time passing
        _timeProvider.Advance(TimeSpan.FromMilliseconds(150));

        // Act
        await _sut.ProcessDownloadAsync(download, _cts.Token);

        // Assert
        // Verify directory was created
        Assert.True(_fileSystem.Directory.Exists("/downloads"));

        // Verify file was written with correct content
        Assert.True(_fileSystem.File.Exists(destinationPath));
        var fileContent = await _fileSystem.File.ReadAllTextAsync(destinationPath);
        Assert.Equal(testContent, fileContent);

        // Verify state updates
        await _stateService.Received(1).UpdateState(downloadId, DownloadState.InProgress);
        await _stateService.Received(1).UpdateState(downloadId, DownloadState.Completed);

        // Verify progress updates (at least 2 - initial and final)
        await _stateService.ReceivedWithAnyArgs(2).UpdateProgress(default, default, default, default);

        // Verify bandwidth usage recorded
        _bandwidthLimiter.Received(1).RecordDownload("example.com", testBytes.Length);
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
        await _stateService.Received(1).UpdateState(downloadId, DownloadState.InProgress);
        await _stateService.Received(1).UpdateState(
            downloadId,
            DownloadState.Failed,
            Arg.Is<string>(s => s.Contains("404")));

        // Verify file was not created
        Assert.False(_fileSystem.File.Exists(destinationPath));
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
        var downloadCts = new CancellationTokenSource();
        _downloadService.GetDownloadToken(downloadId).Returns(downloadCts.Token);

        // Configure HTTP response to be slow
        _messageHandler.DelayBeforeResponse = TimeSpan.FromMilliseconds(100);
        _messageHandler.ResponseToReturn = new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[1024])
        };

        // Start the download
        var downloadTask = _sut.ProcessDownloadAsync(download, _cts.Token);

        // Cancel the download after a short delay
        await Task.Delay(50);
        await downloadCts.CancelAsync();

        // Wait for the download to complete
        await downloadTask;

        // Assert
        // Verify state updates
        await _stateService.Received(1).UpdateState(downloadId, DownloadState.InProgress);
        await _stateService.Received(1).UpdateState(downloadId, DownloadState.Canceled);
    }
}

public class TestHttpMessageHandler : HttpMessageHandler
{
    public HttpResponseMessage? ResponseToReturn { get; set; }
    public Exception? ExceptionToThrow { get; set; }
    public TimeSpan DelayBeforeResponse { get; set; } = TimeSpan.Zero;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
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
