using System.Collections.Concurrent;
using Octans.Core.Downloaders;
using Microsoft.Extensions.Logging;

namespace Octans.Core.Downloads;

public interface IDownloadService
{
    Task<Guid> QueueDownloadAsync(DownloadRequest request);
    Task CancelDownloadAsync(Guid id);
    Task PauseDownloadAsync(Guid id);
    Task ResumeDownloadAsync(Guid id);
    Task RetryDownloadAsync(Guid id);
    CancellationToken GetDownloadToken(Guid downloadId);
}

public sealed class DownloadService(
    IDownloadQueue queue,
    IDownloadStateService stateService,
    ILogger<DownloadService> logger) : IDownloadService, IDisposable, IAsyncDisposable
{
    private readonly CancellationTokenSource _globalCancellation = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _downloadCancellations = new();

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

        var status = new DownloadStatus
        {
            Id = id,
            Url = request.Url.ToString(),
            Filename = filename,
            DestinationPath = request.DestinationPath,
            State = DownloadState.Queued,
            CreatedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow,
            Domain = domain
        };

        // Add to state service for UI visibility
        await stateService.AddOrUpdateDownloadAsync(status);

        // Add to persistent queue
        await queue.EnqueueAsync(new()
        {
            Id = id,
            Url = request.Url.ToString(),
            DestinationPath = request.DestinationPath,
            QueuedAt = DateTime.UtcNow,
            Priority = request.Priority,
            Domain = domain
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
        CancelDownloadToken(id);

        // Update state
        await stateService.UpdateState(id, DownloadState.Canceled);

        logger.LogDebug("Download canceled");
    }

    public Task PauseDownloadAsync(Guid id)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object?> { ["DownloadId"] = id });
        logger.LogInformation("Pausing download");

        // TODO: Implement true pause/resume support.
        // For now, we'll implement pause as cancel since we don't support resuming partial downloads
        CancelDownloadToken(id);
        stateService.UpdateState(id, DownloadState.Paused);

        logger.LogDebug("Download paused");
        return Task.CompletedTask;
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
                QueuedAt = DateTime.UtcNow,
                Domain = status.Domain
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
                QueuedAt = DateTime.UtcNow,
                Domain = status.Domain
            });

            await stateService.UpdateState(id, DownloadState.Queued);
            logger.LogDebug("Download reset and re-queued");
        }
        else
        {
            logger.LogWarning("Cannot retry download - not in failed or canceled state. Current state: {State}", status?.State);
        }
    }

    private void CancelDownloadToken(Guid id)
    {
        if (!_downloadCancellations.TryGetValue(id, out var cts))
        {
            logger.LogDebug("No active cancellation token found for download {DownloadId}", id);
            return;
        }

        logger.LogDebug("Canceling download token for {DownloadId}", id);
        
        cts.Cancel();
        cts.Dispose();
        
        _downloadCancellations.Remove(id, out var _);
    }

    public CancellationToken GetDownloadToken(Guid downloadId)
    {
        if (_downloadCancellations.TryGetValue(downloadId, out var cts))
        {
            logger.LogDebug("Reusing existing cancellation token for download {DownloadId}", downloadId);
            return cts.Token;
        }

        logger.LogDebug("Creating new cancellation token for download {DownloadId}", downloadId);
        
        cts = CancellationTokenSource.CreateLinkedTokenSource(_globalCancellation.Token);
        
        _downloadCancellations[downloadId] = cts;

        return cts.Token;
    }

    public void Dispose()
    {
        logger.LogInformation("Disposing DownloadService and canceling all downloads");
        
        _globalCancellation.Cancel();
        _globalCancellation.Dispose();
        
        foreach (var cts in _downloadCancellations.Values)
        {
            cts.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _globalCancellation.CancelAsync();
        _globalCancellation.Dispose();
        
        foreach (var cts in _downloadCancellations.Values)
        {
            cts.Dispose();
        }
    }
}
