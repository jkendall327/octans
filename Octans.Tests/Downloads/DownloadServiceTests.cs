using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Octans.Core.Downloads;
using Octans.Core.Downloaders;

namespace Octans.Tests.Downloads;

public class DownloadServiceTests
{
    private readonly IDownloadQueue _mockQueue = Substitute.For<IDownloadQueue>();
    private readonly IDownloadStateService _mockStateService = Substitute.For<IDownloadStateService>();
    private readonly ILogger<DownloadService> _logger = NullLogger<DownloadService>.Instance;
    private readonly DownloadService _service;

    public DownloadServiceTests()
    {
        _service = new(_mockQueue, _mockStateService, _logger);
    }

    [Fact]
    public async Task QueueDownloadAsync_ShouldCreateNewDownload()
    {
        var request = new DownloadRequest
        {
            Url = new("https://example.com/file.zip"),
            DestinationPath = "/downloads/file.zip",
            Priority = 1
        };

        var id = await _service.QueueDownloadAsync(request);

        Assert.NotEqual(Guid.Empty, id);

        await _mockStateService.Received(1).AddOrUpdateDownloadAsync(Arg.Is<DownloadStatus>(ds =>
            ds.Id == id &&
            ds.Url == request.Url.ToString() &&
            ds.DestinationPath == request.DestinationPath &&
            ds.State == DownloadState.Queued &&
            ds.Domain == "example.com"));

        await _mockQueue.Received(1).EnqueueAsync(Arg.Is<QueuedDownload>(qd =>
            qd.Id == id &&
            qd.Url == request.Url.ToString() &&
            qd.DestinationPath == request.DestinationPath &&
            qd.Priority == request.Priority &&
            qd.Domain == "example.com"));
    }

    [Fact]
    public async Task CancelDownloadAsync_ShouldCancelAndUpdateState()
    {
        var id = Guid.NewGuid();

        await _service.CancelDownloadAsync(id);

        await _mockQueue.Received(1).RemoveAsync(id);
        await _mockStateService.Received(1).UpdateState(id, DownloadState.Canceled);
    }

    [Fact]
    public async Task PauseDownloadAsync_ShouldUpdateState()
    {
        var id = Guid.NewGuid();

        await _service.PauseDownloadAsync(id);

        await _mockStateService.Received(1).UpdateState(id, DownloadState.Paused);
    }

    [Fact]
    public async Task ResumeDownloadAsync_WhenPaused_ShouldRequeueAndUpdateState()
    {
        var id = Guid.NewGuid();

        var status = new DownloadStatus
        {
            Id = id,
            Url = "https://example.com/file.zip",
            DestinationPath = "/downloads/file.zip",
            State = DownloadState.Paused,
            Domain = "example.com",
            Filename = string.Empty
        };

        _mockStateService.GetDownloadById(id).Returns(status);

        await _service.ResumeDownloadAsync(id);

        await _mockQueue.Received(1).EnqueueAsync(Arg.Is<QueuedDownload>(qd =>
            qd.Id == id &&
            qd.Url == status.Url &&
            qd.DestinationPath == status.DestinationPath));

        await _mockStateService.Received(1).UpdateState(id, DownloadState.Queued);
    }

    [Fact]
    public async Task ResumeDownloadAsync_WhenNotPaused_ShouldNotRequeue()
    {
        var id = Guid.NewGuid();

        var status = new DownloadStatus
        {
            Id = id,
            Url = "https://example.com/file.zip",
            DestinationPath = "/downloads/file.zip",
            State = DownloadState.Failed,
            Domain = "example.com",
            Filename = string.Empty
        };

        _mockStateService.GetDownloadById(id).Returns(status);

        await _service.ResumeDownloadAsync(id);

        await _mockQueue.DidNotReceiveWithAnyArgs().EnqueueAsync(null!);
        await _mockStateService.DidNotReceive().UpdateState(id, Arg.Any<DownloadState>());
    }

    [Fact]
    public async Task RetryDownloadAsync_WhenFailed_ShouldResetAndRequeue()
    {
        var id = Guid.NewGuid();

        var status = new DownloadStatus
        {
            Id = id,
            Url = "https://example.com/file.zip",
            DestinationPath = "/downloads/file.zip",
            State = DownloadState.Failed,
            BytesDownloaded = 1024,
            CurrentSpeed = 100,
            ErrorMessage = "Connection error",
            Domain = "example.com",
            Filename = string.Empty
        };

        _mockStateService.GetDownloadById(id).Returns(status);

        await _service.RetryDownloadAsync(id);

        Assert.Equal(0, status.BytesDownloaded);
        Assert.Equal(0, status.CurrentSpeed);
        Assert.Null(status.ErrorMessage);
        Assert.Null(status.StartedAt);
        Assert.Null(status.CompletedAt);

        await _mockQueue.Received(1).EnqueueAsync(Arg.Is<QueuedDownload>(qd =>
            qd.Id == id &&
            qd.Url == status.Url &&
            qd.DestinationPath == status.DestinationPath));

        await _mockStateService.Received(1).UpdateState(id, DownloadState.Queued);
    }

    [Fact]
    public async Task RetryDownloadAsync_WhenCanceled_ShouldResetAndRequeue()
    {
        var id = Guid.NewGuid();
        var status = new DownloadStatus
        {
            Id = id,
            Url = "https://example.com/file.zip",
            DestinationPath = "/downloads/file.zip",
            State = DownloadState.Canceled,
            Domain = "example.com",
            Filename = string.Empty
        };

        _mockStateService.GetDownloadById(id).Returns(status);

        await _service.RetryDownloadAsync(id);

        await _mockQueue.Received(1).EnqueueAsync(Arg.Any<QueuedDownload>());
        await _mockStateService.Received(1).UpdateState(id, DownloadState.Queued);
    }

    [Fact]
    public async Task RetryDownloadAsync_WhenNotFailedOrCanceled_ShouldNotRequeue()
    {
        var id = Guid.NewGuid();
        var status = new DownloadStatus
        {
            Id = id,
            Url = "https://example.com/file.zip",
            DestinationPath = "/downloads/file.zip",
            State = DownloadState.InProgress,
            Domain = "example.com",
            Filename = string.Empty
        };

        _mockStateService.GetDownloadById(id).Returns(status);

        await _service.RetryDownloadAsync(id);

        await _mockQueue.DidNotReceive().EnqueueAsync(Arg.Any<QueuedDownload>());
        await _mockStateService.DidNotReceive().UpdateState(id, Arg.Any<DownloadState>());
    }

    [Fact]
    public void GetDownloadToken_ShouldReturnCancellationToken()
    {
        var id = Guid.NewGuid();

        var token = _service.GetDownloadToken(id);

        Assert.False(token == CancellationToken.None);
        Assert.False(token.IsCancellationRequested);
    }

    [Fact]
    public async Task CancelDownloadAsync_ShouldCancelToken()
    {
        var id = Guid.NewGuid();
        var token = _service.GetDownloadToken(id);

        await _service.CancelDownloadAsync(id);

        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public void DisposingTheService_ShouldCancelAllActiveDownloads()
    {
        var id = Guid.NewGuid();
        var token = _service.GetDownloadToken(id);

        _service.Dispose();

        Assert.True(token.IsCancellationRequested);
    }
}
