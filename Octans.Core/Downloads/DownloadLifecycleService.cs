using Microsoft.Extensions.Logging;
using Octans.Core.Downloads.Models;
using Octans.Data.Models;

namespace Octans.Core.Downloads;

public interface IDownloadLifecycleService
{
    Task<Guid> QueueDownloadAsync(DownloadRequest request);
    Task CancelDownloadAsync(Guid id);
    Task PauseDownloadAsync(Guid id);
    Task ResumeDownloadAsync(Guid id);
    Task RetryDownloadAsync(Guid id);
    Task<bool> MarkInProgressAsync(Guid id);
    Task MarkCompletedAsync(Guid id);
    Task MarkFailedAsync(Guid id, string errorMessage);
    Task MarkCanceledAsync(Guid id);
}

public sealed class DownloadLifecycleService(
    IDownloadQueue queue,
    IDownloadStateService stateService,
    IActiveDownloadRegistry activeDownloads,
    IDownloadCompletionNotifier completionNotifier,
    TimeProvider timeProvider,
    ILogger<DownloadLifecycleService> logger) : IDownloadLifecycleService
{
    private static readonly HashSet<DownloadState> CanStartStates =
    [
        DownloadState.Queued,
        DownloadState.WaitingForBandwidth
    ];

    private static readonly HashSet<DownloadState> ActiveState =
    [
        DownloadState.InProgress
    ];

    public async Task<Guid> QueueDownloadAsync(DownloadRequest request)
    {
        var id = Guid.NewGuid();
        var filename = Path.GetFileName(request.DestinationPath);
        var domain = request.Url.Host;

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["DownloadId"] = id,
            ["Url"] = request.Url,
            ["Domain"] = domain
        });

        logger.LogInformation("Queueing download for {Filename}", filename);

        var now = timeProvider.GetUtcNow();
        var status = new DownloadStatus
        {
            Id = id,
            Url = request.Url.ToString(),
            Filename = filename,
            DisplayName = request.DisplayName,
            DestinationPath = request.DestinationPath,
            Priority = request.Priority,
            State = DownloadState.Queued,
            CreatedAt = now,
            LastUpdated = now,
            Domain = domain,
            SourceType = request.SourceType,
            SourceId = request.SourceId
        };

        await stateService.QueueDownloadAsync(status);

        logger.LogDebug("Download queued successfully");
        return id;
    }

    public async Task CancelDownloadAsync(Guid id)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object?> { ["DownloadId"] = id });
        logger.LogInformation("Canceling download");

        await queue.RemoveAsync(id);
        await stateService.UpdateState(id, DownloadState.Canceled);
        activeDownloads.Cancel(id);

        logger.LogDebug("Download canceled");
    }

    public async Task PauseDownloadAsync(Guid id)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object?> { ["DownloadId"] = id });
        logger.LogInformation("Pausing download");

        await queue.RemoveAsync(id);
        await stateService.UpdateState(id, DownloadState.Paused);

        // Pause currently stops active transfer and resumes from the beginning later.
        // Range-based partial resume will need explicit temp-file support.
        activeDownloads.Cancel(id);

        logger.LogDebug("Download paused");
    }

    public async Task ResumeDownloadAsync(Guid id)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object?> { ["DownloadId"] = id });
        logger.LogInformation("Resuming download");

        var queued = await stateService.TryRequeuePausedDownloadAsync(id);
        if (!queued)
        {
            logger.LogWarning(
                "Cannot resume download - not in paused state. Current state: {State}",
                stateService.GetDownloadById(id)?.State);
            return;
        }

        logger.LogDebug("Download resumed and re-queued");
    }

    public async Task RetryDownloadAsync(Guid id)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object?> { ["DownloadId"] = id });
        logger.LogInformation("Retrying download");

        var queued = await stateService.TryRequeueFailedOrCanceledDownloadAsync(id);
        if (!queued)
        {
            logger.LogWarning(
                "Cannot retry download - not in failed or canceled state. Current state: {State}",
                stateService.GetDownloadById(id)?.State);
            return;
        }

        logger.LogDebug("Download reset and re-queued");
    }

    public Task<bool> MarkInProgressAsync(Guid id)
    {
        return stateService.TryUpdateState(id, CanStartStates, DownloadState.InProgress);
    }

    public async Task MarkCompletedAsync(Guid id)
    {
        var completed = await stateService.TryUpdateState(id, ActiveState, DownloadState.Completed);
        activeDownloads.Release(id);

        if (!completed)
        {
            logger.LogDebug("Skipped completion because download is no longer active");
            return;
        }

        var status = stateService.GetDownloadById(id);
        if (status is not null)
        {
            await completionNotifier.DownloadCompletedAsync(status);
        }
    }

    public async Task MarkFailedAsync(Guid id, string errorMessage)
    {
        await stateService.TryUpdateState(id, ActiveState, DownloadState.Failed, errorMessage);
        activeDownloads.Release(id);
    }

    public async Task MarkCanceledAsync(Guid id)
    {
        await stateService.TryUpdateState(id, ActiveState, DownloadState.Canceled);
        activeDownloads.Release(id);
    }

}
