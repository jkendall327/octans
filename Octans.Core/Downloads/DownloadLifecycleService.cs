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
    Task MarkCompletedAsync(Guid id, DownloadTerminalUpdate? terminalUpdate = null);
    Task MarkFailedAsync(
        Guid id,
        string errorMessage,
        DownloadFailureCategory failureCategory = DownloadFailureCategory.Unknown,
        DownloadTerminalOutcome outcome = DownloadTerminalOutcome.Failed,
        int? httpStatusCode = null,
        string? validationMessage = null,
        string? responseContentType = null);
    Task MarkCanceledAsync(Guid id);
}

public sealed class DownloadLifecycleService(
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
            AllowedContentTypes = DownloadContentTypeList.Serialize(request.AllowedContentTypes),
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

        await stateService.CancelDownloadAsync(id);
        activeDownloads.Cancel(id);
        await NotifyFinishedAsync(id);

        logger.LogDebug("Download canceled");
    }

    public async Task PauseDownloadAsync(Guid id)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object?> { ["DownloadId"] = id });
        logger.LogInformation("Pausing download");

        await stateService.PauseDownloadAsync(id);

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

    public async Task MarkCompletedAsync(Guid id, DownloadTerminalUpdate? terminalUpdate = null)
    {
        terminalUpdate ??= new()
        {
            Outcome = DownloadTerminalOutcome.Completed
        };

        var completed = await stateService.TryUpdateState(
            id,
            ActiveState,
            DownloadState.Completed,
            terminalUpdate: terminalUpdate);
        activeDownloads.Release(id);

        if (!completed)
        {
            logger.LogDebug("Skipped completion because download is no longer active");
            return;
        }

        await NotifyFinishedAsync(id);
    }

    public async Task MarkFailedAsync(
        Guid id,
        string errorMessage,
        DownloadFailureCategory failureCategory = DownloadFailureCategory.Unknown,
        DownloadTerminalOutcome outcome = DownloadTerminalOutcome.Failed,
        int? httpStatusCode = null,
        string? validationMessage = null,
        string? responseContentType = null)
    {
        var failed = await stateService.TryUpdateState(
            id,
            ActiveState,
            DownloadState.Failed,
            errorMessage,
            new()
            {
                Outcome = outcome,
                FailureCategory = failureCategory,
                HttpStatusCode = httpStatusCode,
                ValidationMessage = validationMessage,
                ResponseContentType = responseContentType
            });
        activeDownloads.Release(id);

        if (!failed)
        {
            return;
        }

        await NotifyFinishedAsync(id);
    }

    public async Task MarkCanceledAsync(Guid id)
    {
        var canceled = await stateService.TryUpdateState(
            id,
            ActiveState,
            DownloadState.Canceled,
            terminalUpdate: new()
            {
                Outcome = DownloadTerminalOutcome.Canceled
            });
        activeDownloads.Release(id);

        if (!canceled)
        {
            return;
        }

        await NotifyFinishedAsync(id);
    }

    private async Task NotifyFinishedAsync(Guid id)
    {
        var status = stateService.GetDownloadById(id);
        var result = status is null ? null : DownloadJobResults.FromStatus(status);
        if (result is not null)
        {
            await completionNotifier.DownloadFinishedAsync(result);
        }
    }

}
