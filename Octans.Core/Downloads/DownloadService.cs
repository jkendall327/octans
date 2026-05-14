using Microsoft.Extensions.Logging;
using Octans.Core.Downloads.Models;
using Octans.Data.Models;

namespace Octans.Core.Downloads;

public interface IDownloadService
{
    Task<Guid> QueueDownloadAsync(DownloadRequest request);
    Task CancelDownloadAsync(Guid id);
    Task PauseDownloadAsync(Guid id);
    Task ResumeDownloadAsync(Guid id);
    Task RetryDownloadAsync(Guid id);
}

public sealed class DownloadService(
    IDownloadQueue queue,
    IDownloadStateService stateService,
    IActiveDownloadRegistry activeDownloads,
    TimeProvider timeProvider,
    ILogger<DownloadService> logger) : IDownloadService
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

        // Add to state service for UI visibility
        await stateService.AddOrUpdateDownloadAsync(status);

        // Add to persistent queue
        await queue.EnqueueAsync(new()
        {
            Id = id,
            Url = request.Url.ToString(),
            DestinationPath = request.DestinationPath,
            QueuedAt = now,
            Priority = request.Priority,
            Domain = domain,
            DisplayName = request.DisplayName,
            SourceType = request.SourceType,
            SourceId = request.SourceId
        });

        logger.LogDebug("Download queued successfully");
        return id;
    }

    public async Task CancelDownloadAsync(Guid id)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object?> { ["DownloadId"] = id });
        logger.LogInformation("Canceling download");

        // First, try to remove from queue if it's still queued
        await queue.RemoveAsync(id);

        // Then cancel if it's in progress
        activeDownloads.Cancel(id);

        // Update state
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

        if (status is { State: DownloadState.Paused })
        {
            // Re-queue the download
            await queue.EnqueueAsync(new()
            {
                Id = id,
                Url = status.Url,
                DestinationPath = status.DestinationPath,
                QueuedAt = timeProvider.GetUtcNow(),
                Domain = status.Domain,
                DisplayName = status.DisplayName,
                SourceType = status.SourceType,
                SourceId = status.SourceId
            });

            await stateService.UpdateState(id, DownloadState.Queued);
            logger.LogDebug("Download resumed and re-queued");
        }
        else
        {
            logger.LogWarning("Cannot resume download - not in paused state. Current state: {State}", status?.State);
        }
    }

    public async Task RetryDownloadAsync(Guid id)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object?> { ["DownloadId"] = id });
        logger.LogInformation("Retrying download");

        var status = stateService.GetDownloadById(id);
        if (status is { State: DownloadState.Failed or DownloadState.Canceled })
        {
            // Reset download state
            status.BytesDownloaded = 0;
            status.CurrentSpeed = 0;
            status.ErrorMessage = null;
            status.StartedAt = null;
            status.CompletedAt = null;

            // Re-queue the download
            await queue.EnqueueAsync(new()
            {
                Id = id,
                Url = status.Url,
                DestinationPath = status.DestinationPath,
                QueuedAt = timeProvider.GetUtcNow(),
                Domain = status.Domain,
                DisplayName = status.DisplayName,
                SourceType = status.SourceType,
                SourceId = status.SourceId
            });

            await stateService.UpdateState(id, DownloadState.Queued);
            logger.LogDebug("Download reset and re-queued");
        }
        else
        {
            logger.LogWarning("Cannot retry download - not in failed or canceled state. Current state: {State}", status?.State);
        }
    }

}
