using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Octans.Core.Downloads;
using Octans.Core.Downloads.Models;
using Octans.Data.Models;
using Octans.Tests.Helpers;

namespace Octans.Tests.Downloads;

public class DownloadServiceTests
{
    private readonly IDownloadQueue _mockQueue = Substitute.For<IDownloadQueue>();
    private readonly IDownloadStateService _mockStateService = Substitute.For<IDownloadStateService>();
    private readonly IActiveDownloadRegistry _activeDownloads = Substitute.For<IActiveDownloadRegistry>();
    private readonly IDownloadCompletionNotifier _completionNotifier = Substitute.For<IDownloadCompletionNotifier>();
    private readonly DownloadLifecycleService _lifecycle;
    private readonly DownloadService _service;

    public DownloadServiceTests()
    {
        var timeProvider = new FakeTimeProvider(TestClock.UtcNow);
        _lifecycle = new(
            _mockQueue,
            _mockStateService,
            _activeDownloads,
            _completionNotifier,
            timeProvider,
            NullLogger<DownloadLifecycleService>.Instance);

        _service = new(_lifecycle);
    }

    [Fact]
    public async Task QueueDownloadAsync_ShouldCreateNewDownload()
    {
        var request = new DownloadRequest
        {
            Url = new("https://example.com/file.zip"),
            DestinationPath = "/downloads/file.zip",
            DisplayName = "Example file",
            SourceType = "Test",
            SourceId = "test-source",
            Priority = 1
        };

        var id = await _service.QueueDownloadAsync(request);

        Assert.NotEqual(Guid.Empty, id);

        await _mockStateService.Received(1).QueueDownloadAsync(Arg.Is<DownloadStatus>(ds =>
            ds.Id == id &&
            ds.Url == request.Url.ToString() &&
            ds.DestinationPath == request.DestinationPath &&
            ds.DisplayName == request.DisplayName &&
            ds.Priority == request.Priority &&
            ds.State == DownloadState.Queued &&
            ds.Domain == "example.com" &&
            ds.SourceType == request.SourceType &&
            ds.SourceId == request.SourceId));
    }

    [Fact]
    public async Task CancelDownloadAsync_ShouldCancelAndUpdateState()
    {
        var id = Guid.NewGuid();

        await _service.CancelDownloadAsync(id);

        await _mockQueue.Received(1).RemoveAsync(id);
        _activeDownloads.Received(1).Cancel(id);
        await _mockStateService.Received(1).UpdateState(id, DownloadState.Canceled);
    }

    [Fact]
    public async Task PauseDownloadAsync_ShouldUpdateState()
    {
        var id = Guid.NewGuid();

        await _service.PauseDownloadAsync(id);

        await _mockQueue.Received(1).RemoveAsync(id);
        _activeDownloads.Received(1).Cancel(id);
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
        _mockStateService.TryRequeuePausedDownloadAsync(id).Returns(true);

        await _service.ResumeDownloadAsync(id);

        await _mockStateService.Received(1).TryRequeuePausedDownloadAsync(id);
        await _mockStateService.DidNotReceive().UpdateState(id, DownloadState.Queued);
        await _mockQueue.DidNotReceive().EnqueueAsync(Arg.Any<QueuedDownload>());
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
        await _mockStateService.Received(1).TryRequeuePausedDownloadAsync(id);
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
        _mockStateService.TryRequeueFailedOrCanceledDownloadAsync(id).Returns(true);

        await _service.RetryDownloadAsync(id);

        await _mockStateService.Received(1).TryRequeueFailedOrCanceledDownloadAsync(id);
        await _mockStateService.DidNotReceive().UpdateState(id, DownloadState.Queued);
        await _mockQueue.DidNotReceive().EnqueueAsync(Arg.Any<QueuedDownload>());
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
        _mockStateService.TryRequeueFailedOrCanceledDownloadAsync(id).Returns(true);

        await _service.RetryDownloadAsync(id);

        await _mockStateService.Received(1).TryRequeueFailedOrCanceledDownloadAsync(id);
        await _mockQueue.DidNotReceive().EnqueueAsync(Arg.Any<QueuedDownload>());
        await _mockStateService.DidNotReceive().UpdateState(id, DownloadState.Queued);
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
        await _mockStateService.Received(1).TryRequeueFailedOrCanceledDownloadAsync(id);
    }

    [Fact]
    public async Task MarkCompletedAsync_ShouldUpdateStateAndReleaseActiveToken()
    {
        var id = Guid.NewGuid();
        var status = new DownloadStatus
        {
            Id = id,
            Url = "https://example.com/file.zip",
            Filename = "file.zip",
            DestinationPath = "/downloads/file.zip",
            State = DownloadState.InProgress,
            Domain = "example.com"
        };

        _mockStateService.GetDownloadById(id).Returns(status);
        _mockStateService
            .TryUpdateState(
                id,
                Arg.Is<IReadOnlySet<DownloadState>>(states => IsOnlyInProgress(states)),
                DownloadState.Completed)
            .Returns(true);

        await _lifecycle.MarkCompletedAsync(id);

        await _mockStateService
            .Received(1)
            .TryUpdateState(
                id,
                Arg.Is<IReadOnlySet<DownloadState>>(states => IsOnlyInProgress(states)),
                DownloadState.Completed);
        _activeDownloads.Received(1).Release(id);
        await _completionNotifier.Received(1).DownloadCompletedAsync(status);
    }

    [Fact]
    public async Task MarkFailedAsync_ShouldUpdateStateAndReleaseActiveToken()
    {
        var id = Guid.NewGuid();

        await _lifecycle.MarkFailedAsync(id, "Network broke");

        await _mockStateService
            .Received(1)
            .TryUpdateState(
                id,
                Arg.Is<IReadOnlySet<DownloadState>>(states => IsOnlyInProgress(states)),
                DownloadState.Failed,
                "Network broke");
        _activeDownloads.Received(1).Release(id);
    }

    [Fact]
    public async Task MarkCanceledAsync_ShouldUpdateStateAndReleaseActiveToken()
    {
        var id = Guid.NewGuid();

        await _lifecycle.MarkCanceledAsync(id);

        await _mockStateService
            .Received(1)
            .TryUpdateState(
                id,
                Arg.Is<IReadOnlySet<DownloadState>>(states => IsOnlyInProgress(states)),
                DownloadState.Canceled);
        _activeDownloads.Received(1).Release(id);
    }

    [Fact]
    public async Task MarkCompletedAsync_WhenStateTransitionIsRejected_DoesNotNotifyCompletion()
    {
        var id = Guid.NewGuid();

        _mockStateService
            .TryUpdateState(
                id,
                Arg.Is<IReadOnlySet<DownloadState>>(states => IsOnlyInProgress(states)),
                DownloadState.Completed)
            .Returns(false);

        await _lifecycle.MarkCompletedAsync(id);

        _activeDownloads.Received(1).Release(id);
        await _completionNotifier.DidNotReceive().DownloadCompletedAsync(Arg.Any<DownloadStatus>());
    }

    private static bool IsOnlyInProgress(IReadOnlySet<DownloadState> states)
    {
        return states.SetEquals(new[] { DownloadState.InProgress });
    }
}
