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
        using var downloadCts = new CancellationTokenSource();
        _downloadService.GetDownloadToken(downloadId).Returns(downloadCts.Token);

        _messageHandler.WaitForCancellation = true;

        // Start the download
        var downloadTask = _sut.ProcessDownloadAsync(download, _cts.Token);

        await _messageHandler.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await downloadCts.CancelAsync();

        // Wait for the download to complete
        await downloadTask;

        // Assert
        // Verify state updates
        await _stateService.Received(1).UpdateState(downloadId, DownloadState.InProgress);
        await _stateService.Received(1).UpdateState(downloadId, DownloadState.Canceled);
        await _stateService.DidNotReceive().UpdateState(downloadId, DownloadState.Completed);
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
