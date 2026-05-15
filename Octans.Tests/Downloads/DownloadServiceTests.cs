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
    private readonly IDownloadStateService _mockStateService = Substitute.For<IDownloadStateService>();
    private readonly IActiveDownloadRegistry _activeDownloads = Substitute.For<IActiveDownloadRegistry>();
    private readonly IDownloadCompletionNotifier _completionNotifier = Substitute.For<IDownloadCompletionNotifier>();
    private readonly IDownloadJobResultService _jobResults = Substitute.For<IDownloadJobResultService>();
    private readonly DownloadLifecycleService _lifecycle;
    private readonly DownloadService _service;

    public DownloadServiceTests()
    {
        var timeProvider = new FakeTimeProvider(TestClock.UtcNow);
        _lifecycle = new(
            _mockStateService,
            _activeDownloads,
            _completionNotifier,
            timeProvider,
            NullLogger<DownloadLifecycleService>.Instance);

        _service = new(_lifecycle, _jobResults);
    }

    [Fact]
    public async Task QueueDownloadJobAsync_ShouldReturnTypedHandle()
    {
        var request = new DownloadRequest
        {
            Url = new("https://example.com/file.zip"),
            DestinationPath = "/downloads/file.zip"
        };

        var handle = await _service.QueueDownloadJobAsync(request);

        Assert.NotEqual(Guid.Empty, handle.Id);
        await _mockStateService.Received(1).QueueDownloadAsync(Arg.Is<DownloadStatus>(ds => ds.Id == handle.Id));
    }

    [Fact]
    public async Task GetResultAsync_ShouldDelegateToJobResultService()
    {
        var handle = new DownloadJobHandle(Guid.NewGuid());
        var result = new DownloadJobResult
        {
            DownloadId = handle.Id,
            Outcome = DownloadTerminalOutcome.Completed,
            Url = "https://example.com/file.zip",
            DestinationPath = "/downloads/file.zip"
        };

        _jobResults.GetResultAsync(handle, Arg.Any<CancellationToken>()).Returns(result);

        var actual = await _service.GetResultAsync(handle);

        Assert.Same(result, actual);
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
            Priority = 1,
            AllowedContentTypes = { "image/*" }
        };

        var id = await _service.QueueDownloadAsync(request);

        Assert.NotEqual(Guid.Empty, id);

        await _mockStateService.Received(1).QueueDownloadAsync(Arg.Is<DownloadStatus>(ds =>
            ds.Id == id &&
            ds.Url == request.Url.ToString() &&
            ds.DestinationPath == request.DestinationPath &&
            ds.DisplayName == request.DisplayName &&
            ds.AllowedContentTypes != null &&
            ds.AllowedContentTypes.Contains("image/*") &&
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
        var status = new DownloadStatus
        {
            Id = id,
            Url = "https://example.com/file.zip",
            Filename = "file.zip",
            DestinationPath = "/downloads/file.zip",
            State = DownloadState.Canceled,
            Domain = "example.com",
            TerminalOutcome = DownloadTerminalOutcome.Canceled
        };

        _mockStateService.GetDownloadById(id).Returns(status);

        await _service.CancelDownloadAsync(id);

        await _mockStateService.Received(1).CancelDownloadAsync(id);
        _activeDownloads.Received(1).Cancel(id);
        await _completionNotifier.Received(1).DownloadFinishedAsync(Arg.Is<DownloadJobResult>(result =>
            result.DownloadId == id &&
            result.Outcome == DownloadTerminalOutcome.Canceled));
    }

    [Fact]
    public async Task PauseDownloadAsync_ShouldUpdateState()
    {
        var id = Guid.NewGuid();

        await _service.PauseDownloadAsync(id);

        await _mockStateService.Received(1).PauseDownloadAsync(id);
        _activeDownloads.Received(1).Cancel(id);
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
            State = DownloadState.Completed,
            Domain = "example.com",
            TerminalOutcome = DownloadTerminalOutcome.Completed
        };

        _mockStateService.GetDownloadById(id).Returns(status);
        _mockStateService
            .TryUpdateState(
                id,
                Arg.Is<IReadOnlySet<DownloadState>>(states => IsOnlyInProgress(states)),
                DownloadState.Completed,
                terminalUpdate: Arg.Any<DownloadTerminalUpdate>())
            .Returns(true);

        await _lifecycle.MarkCompletedAsync(id);

        await _mockStateService
            .Received(1)
            .TryUpdateState(
                id,
                Arg.Is<IReadOnlySet<DownloadState>>(states => IsOnlyInProgress(states)),
                DownloadState.Completed,
                terminalUpdate: Arg.Is<DownloadTerminalUpdate>(update =>
                    update.Outcome == DownloadTerminalOutcome.Completed));
        _activeDownloads.Received(1).Release(id);
        await _completionNotifier.Received(1).DownloadFinishedAsync(Arg.Is<DownloadJobResult>(result =>
            result.DownloadId == id &&
            result.Outcome == DownloadTerminalOutcome.Completed));
    }

    [Fact]
    public async Task MarkFailedAsync_ShouldUpdateStateReleaseActiveTokenAndNotify()
    {
        var id = Guid.NewGuid();
        var status = new DownloadStatus
        {
            Id = id,
            Url = "https://example.com/file.zip",
            Filename = "file.zip",
            DestinationPath = "/downloads/file.zip",
            State = DownloadState.Failed,
            Domain = "example.com",
            ErrorMessage = "Network broke",
            TerminalOutcome = DownloadTerminalOutcome.Failed,
            FailureCategory = DownloadFailureCategory.Network
        };

        _mockStateService.GetDownloadById(id).Returns(status);
        _mockStateService
            .TryUpdateState(
                id,
                Arg.Is<IReadOnlySet<DownloadState>>(states => IsOnlyInProgress(states)),
                DownloadState.Failed,
                "Network broke",
                Arg.Any<DownloadTerminalUpdate>())
            .Returns(true);

        await _lifecycle.MarkFailedAsync(id, "Network broke", DownloadFailureCategory.Network);

        await _mockStateService
            .Received(1)
            .TryUpdateState(
                id,
                Arg.Is<IReadOnlySet<DownloadState>>(states => IsOnlyInProgress(states)),
                DownloadState.Failed,
                "Network broke",
                Arg.Is<DownloadTerminalUpdate>(update =>
                    update.Outcome == DownloadTerminalOutcome.Failed &&
                    update.FailureCategory == DownloadFailureCategory.Network));
        _activeDownloads.Received(1).Release(id);
        await _completionNotifier.Received(1).DownloadFinishedAsync(Arg.Is<DownloadJobResult>(result =>
            result.DownloadId == id &&
            result.FailureCategory == DownloadFailureCategory.Network));
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
                DownloadState.Canceled,
                terminalUpdate: Arg.Is<DownloadTerminalUpdate>(update =>
                    update.Outcome == DownloadTerminalOutcome.Canceled));
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
        await _completionNotifier.DidNotReceive().DownloadFinishedAsync(Arg.Any<DownloadJobResult>());
    }

    private static bool IsOnlyInProgress(IReadOnlySet<DownloadState> states)
    {
        return states.SetEquals(new[] { DownloadState.InProgress });
    }
}
