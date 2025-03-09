using Microsoft.EntityFrameworkCore;
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

public class DownloadService : IDownloadService
{
    private readonly IDownloadQueue _queue;
    private readonly DownloadStateService _stateService;
    private readonly CancellationTokenSource _globalCancellation = new();
    private readonly Dictionary<Guid, CancellationTokenSource> _downloadCancellations = new();
    private readonly object _cancellationLock = new();

    public DownloadService(IDownloadQueue queue, DownloadStateService stateService)
    {
        _queue = queue;
        _stateService = stateService;
    }

    public async Task<Guid> QueueDownloadAsync(DownloadRequest request)
    {
        var id = Guid.NewGuid();
        var filename = Path.GetFileName(request.DestinationPath);
        
        Uri uri = new Uri(request.Url);
        string domain = uri.Host;

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
        _stateService.AddOrUpdateDownload(status);
        
        // Add to persistent queue
        await _queue.EnqueueAsync(new QueuedDownload
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
        await _queue.RemoveAsync(id);
        
        // Then cancel if it's in progress
        CancelDownloadToken(id);
        
        // Update state
        _stateService.UpdateState(id, DownloadState.Canceled);
    }

    public async Task PauseDownloadAsync(Guid id)
    {
        // For now, we'll implement pause as cancel since we don't support resuming partial downloads
        CancelDownloadToken(id);
        _stateService.UpdateState(id, DownloadState.Paused);
    }

    public async Task ResumeDownloadAsync(Guid id)
    {
        var status = _stateService.GetDownloadById(id);
        if (status is {State: DownloadState.Paused})
        {
            // Re-queue the download
            await _queue.EnqueueAsync(new QueuedDownload
            {
                Id = id,
                Url = status.Url,
                DestinationPath = status.DestinationPath,
                QueuedAt = DateTime.UtcNow,
                Domain = status.Domain
            });
            
            _stateService.UpdateState(id, DownloadState.Queued);
        }
    }

    public async Task RetryDownloadAsync(Guid id)
    {
        var status = _stateService.GetDownloadById(id);
        if (status is {State: DownloadState.Failed or DownloadState.Canceled})
        {
            // Reset download state
            status.BytesDownloaded = 0;
            status.CurrentSpeed = 0;
            status.ErrorMessage = null;
            status.StartedAt = null;
            status.CompletedAt = null;
            
            // Re-queue the download
            await _queue.EnqueueAsync(new QueuedDownload
            {
                Id = id,
                Url = status.Url,
                DestinationPath = status.DestinationPath,
                QueuedAt = DateTime.UtcNow,
                Domain = status.Domain
            });
            
            _stateService.UpdateState(id, DownloadState.Queued);
        }
    }

    private void CancelDownloadToken(Guid id)
    {
        lock (_cancellationLock)
        {
            if (_downloadCancellations.TryGetValue(id, out var cts))
            {
                cts.Cancel();
                _downloadCancellations.Remove(id);
            }
        }
    }

    public CancellationToken GetDownloadToken(Guid id)
    {
        lock (_cancellationLock)
        {
            if (!_downloadCancellations.TryGetValue(id, out var cts))
            {
                cts = CancellationTokenSource.CreateLinkedTokenSource(_globalCancellation.Token);
                _downloadCancellations[id] = cts;
            }
            
            return cts.Token;
        }
    }
}