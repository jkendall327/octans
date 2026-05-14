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
}

public sealed class DownloadLifecycleService(
    IDownloadQueue queue,
    IDownloadStateService stateService,
    IActiveDownloadRegistry activeDownloads,
    TimeProvider timeProvider,
    ILogger<DownloadLifecycleService> logger) : IDownloadLifecycleService
{
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

        await stateService.AddOrUpdateDownloadAsync(status);
        await queue.EnqueueAsync(BuildQueuedDownload(status, now));

        logger.LogDebug("Download queued successfully");
        return id;
    }

    public async Task CancelDownloadAsync(Guid id)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object?> { ["DownloadId"] = id });
        logger.LogInformation("Canceling download");

        await queue.RemoveAsync(id);
        activeDownloads.Cancel(id);
        await stateService.UpdateState(id, DownloadState.Canceled);

        logger.LogDebug("Download canceled");
    }

    public async Task PauseDownloadAsync(Guid id)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object?> { ["DownloadId"] = id });
        logger.LogInformation("Pausing download");

        await queue.RemoveAsync(id);

        // Pause currently stops active transfer and resumes from the beginning later.
        // Range-based partial resume will need explicit temp-file support.
        activeDownloads.Cancel(id);
        await stateService.UpdateState(id, DownloadState.Paused);

        logger.LogDebug("Download paused");
    }

    public async Task ResumeDownloadAsync(Guid id)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object?> { ["DownloadId"] = id });
        logger.LogInformation("Resuming download");

        var status = stateService.GetDownloadById(id);
        if (status is not { State: DownloadState.Paused })
        {
            logger.LogWarning("Cannot resume download - not in paused state. Current state: {State}", status?.State);
            return;
        }

        await queue.EnqueueAsync(BuildQueuedDownload(status, timeProvider.GetUtcNow()));
        await stateService.UpdateState(id, DownloadState.Queued);

        logger.LogDebug("Download resumed and re-queued");
    }

    public async Task RetryDownloadAsync(Guid id)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object?> { ["DownloadId"] = id });
        logger.LogInformation("Retrying download");

        var status = stateService.GetDownloadById(id);
        if (status is not { State: DownloadState.Failed or DownloadState.Canceled })
        {
            logger.LogWarning("Cannot retry download - not in failed or canceled state. Current state: {State}", status?.State);
            return;
        }

        status.BytesDownloaded = 0;
        status.CurrentSpeed = 0;
        status.ErrorMessage = null;
        status.StartedAt = null;
        status.CompletedAt = null;

        await queue.EnqueueAsync(BuildQueuedDownload(status, timeProvider.GetUtcNow()));
        await stateService.UpdateState(id, DownloadState.Queued);

        logger.LogDebug("Download reset and re-queued");
    }

    private static QueuedDownload BuildQueuedDownload(DownloadStatus status, DateTimeOffset queuedAt) => new()
    {
        Id = status.Id,
        Url = status.Url,
        DestinationPath = status.DestinationPath,
        DisplayName = status.DisplayName,
        QueuedAt = queuedAt,
        Priority = status.Priority,
        Domain = status.Domain,
        SourceType = status.SourceType,
        SourceId = status.SourceId
    };
}
