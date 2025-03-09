using Octans.Core.Downloaders;

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

public class DownloadService(IDownloadQueue queue, DownloadStateService stateService) : IDownloadService
{
    private readonly CancellationTokenSource _globalCancellation = new();
    private readonly Dictionary<Guid, CancellationTokenSource> _downloadCancellations = new();
    private readonly Lock _cancellationLock = new();

    public async Task<Guid> QueueDownloadAsync(DownloadRequest request)
    {
        var id = Guid.NewGuid();
        var filename = Path.GetFileName(request.DestinationPath);

        var uri = new Uri(request.Url);
        var domain = uri.Host;

        var status = new DownloadStatus
        {
            Id = id,
            Url = request.Url,
            Filename = filename,
            DestinationPath = request.DestinationPath,
            State = DownloadState.Queued,
            CreatedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow,
            Domain = domain
        };

        // Add to state service for UI visibility
        stateService.AddOrUpdateDownload(status);

        // Add to persistent queue
        await queue.EnqueueAsync(new()
        {
            Id = id,
            Url = request.Url,
            DestinationPath = request.DestinationPath,
            QueuedAt = DateTime.UtcNow,
            Priority = request.Priority,
            Domain = domain
        });

        return id;
    }

    public async Task CancelDownloadAsync(Guid id)
    {
        // First, try to remove from queue if it's still queued
        await queue.RemoveAsync(id);

        // Then cancel if it's in progress
        CancelDownloadToken(id);

        // Update state
        stateService.UpdateState(id, DownloadState.Canceled);
    }

    public Task PauseDownloadAsync(Guid id)
    {
        // For now, we'll implement pause as cancel since we don't support resuming partial downloads
        CancelDownloadToken(id);
        stateService.UpdateState(id, DownloadState.Paused);

        return Task.CompletedTask;
    }

    public async Task ResumeDownloadAsync(Guid id)
    {
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

            stateService.UpdateState(id, DownloadState.Queued);
        }
    }

    public async Task RetryDownloadAsync(Guid id)
    {
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

            stateService.UpdateState(id, DownloadState.Queued);
        }
    }

    private void CancelDownloadToken(Guid id)
    {
        lock (_cancellationLock)
        {
            if (!_downloadCancellations.TryGetValue(id, out var cts)) return;

            cts.Cancel();
            _downloadCancellations.Remove(id);
        }
    }

    public CancellationToken GetDownloadToken(Guid downloadId)
    {
        lock (_cancellationLock)
        {
            if (_downloadCancellations.TryGetValue(downloadId, out var cts))
            {
                return cts.Token;
            }

            cts = CancellationTokenSource.CreateLinkedTokenSource(_globalCancellation.Token);
            _downloadCancellations[downloadId] = cts;

            return cts.Token;
        }
    }
}