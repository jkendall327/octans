using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Octans.Core.Models;

namespace Octans.Core.Downloaders;

public class DownloadStateService
{
    private readonly Dictionary<Guid, DownloadStatus> _activeDownloads = new();
    private readonly Lock _lock = new();
    private readonly ILogger<DownloadStateService> _logger;
    private readonly ServerDbContext _dbContext;

    public event Action<DownloadStatus>? OnDownloadProgressChanged;
    public event Action? OnDownloadsChanged;

    public DownloadStateService(ILogger<DownloadStateService> logger, ServerDbContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
    }

    public async Task InitializeFromDbAsync()
    {
        var statuses = await _dbContext.DownloadStatuses
            .Where(d => d.State != DownloadState.Completed && d.State != DownloadState.Canceled)
            .ToListAsync();
            
        lock (_lock)
        {
            foreach (var status in statuses)
            {
                _activeDownloads[status.Id] = status;
            }
        }
        
        OnDownloadsChanged?.Invoke();
    }

    public IReadOnlyList<DownloadStatus> GetAllDownloads()
    {
        lock (_lock)
        {
            return _activeDownloads.Values.OrderByDescending(d => d.CreatedAt).ToList();
        }
    }

    public DownloadStatus? GetDownloadById(Guid id)
    {
        lock (_lock)
        {
            return _activeDownloads.TryGetValue(id, out var status) ? status : null;
        }
    }

    public void UpdateProgress(Guid id, long bytesDownloaded, long totalBytes, double speed)
    {
        lock (_lock)
        {
            if (_activeDownloads.TryGetValue(id, out var status))
            {
                status.BytesDownloaded = bytesDownloaded;
                status.TotalBytes = totalBytes;
                status.CurrentSpeed = speed;
                status.LastUpdated = DateTime.UtcNow;
                
                // Notify subscribers
                OnDownloadProgressChanged?.Invoke(status);
            }
        }
    }

    public void UpdateState(Guid id, DownloadState newState, string? errorMessage = null)
    {
        lock (_lock)
        {
            if (_activeDownloads.TryGetValue(id, out var status))
            {
                status.State = newState;
                status.LastUpdated = DateTime.UtcNow;
                
                switch (newState)
                {
                    case DownloadState.InProgress:
                        status.StartedAt ??= DateTime.UtcNow;
                        break;
                    case DownloadState.Completed:
                        status.CompletedAt = DateTime.UtcNow;
                        break;
                    case DownloadState.Failed:
                        status.ErrorMessage = errorMessage;
                        break;
                }
                
                // Persist state change to database
                Task.Run(async () => 
                {
                    await using var scope = _dbContext.Database.BeginTransaction();
                    try
                    {
                        var dbStatus = await _dbContext.DownloadStatuses.FindAsync(id);
                        if (dbStatus != null)
                        {
                            dbStatus.State = status.State;
                            dbStatus.BytesDownloaded = status.BytesDownloaded;
                            dbStatus.TotalBytes = status.TotalBytes;
                            dbStatus.LastUpdated = status.LastUpdated;
                            dbStatus.StartedAt = status.StartedAt;
                            dbStatus.CompletedAt = status.CompletedAt;
                            dbStatus.ErrorMessage = status.ErrorMessage;
                            
                            await _dbContext.SaveChangesAsync();
                            await scope.CommitAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to persist download state change");
                    }
                });
                
                // Notify subscribers
                OnDownloadProgressChanged?.Invoke(status);
                OnDownloadsChanged?.Invoke();
            }
        }
    }

    public void AddOrUpdateDownload(DownloadStatus status)
    {
        lock (_lock)
        {
            _activeDownloads[status.Id] = status;
            
            // Persist to database
            Task.Run(async () => 
            {
                try
                {
                    var existingStatus = await _dbContext.DownloadStatuses.FindAsync(status.Id);
                    if (existingStatus == null)
                    {
                        _dbContext.DownloadStatuses.Add(status);
                    }
                    else
                    {
                        _dbContext.Entry(existingStatus).CurrentValues.SetValues(status);
                    }
                    
                    await _dbContext.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to persist download status");
                }
            });
            
            OnDownloadsChanged?.Invoke();
        }
    }

    public void RemoveDownload(Guid id)
    {
        lock (_lock)
        {
            if (_activeDownloads.Remove(id))
            {
                // Remove from database
                Task.Run(async () => 
                {
                    try
                    {
                        var status = await _dbContext.DownloadStatuses.FindAsync(id);
                        if (status != null)
                        {
                            _dbContext.DownloadStatuses.Remove(status);
                            await _dbContext.SaveChangesAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to remove download status from database");
                    }
                });
                
                OnDownloadsChanged?.Invoke();
            }
        }
    }
}