namespace Octans.Core.Downloaders;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

// 1. Domain Models

public enum DownloadState
{
    Queued,
    WaitingForBandwidth,
    InProgress,
    Paused,
    Completed,
    Failed,
    Canceled
}

public class DownloadStatus
{
    public Guid Id { get; set; }
    public required string Url { get; set; }
    public required string Filename { get; set; }
    public required string DestinationPath { get; set; }
    public long TotalBytes { get; set; }
    public long BytesDownloaded { get; set; }
    public double ProgressPercentage => TotalBytes > 0 ? (double)BytesDownloaded / TotalBytes * 100 : 0;
    public double CurrentSpeed { get; set; } // bytes per second
    public DownloadState State { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime LastUpdated { get; set; }
    public string? ErrorMessage { get; set; }
    public required string Domain { get; set; }
}

public class DownloadRequest
{
    public required string Url { get; set; }
    public required string DestinationPath { get; set; }
    public int Priority { get; set; } = 0; // Higher numbers = higher priority
}

public class QueuedDownload
{
    public Guid Id { get; set; }
    public required string Url { get; set; }
    public required string DestinationPath { get; set; }
    public DateTime QueuedAt { get; set; }
    public int Priority { get; set; }
    public required string Domain { get; set; }
}

// 2. Database Context

public class DownloadDbContext : DbContext
{
    public DownloadDbContext(DbContextOptions<DownloadDbContext> options) : base(options) { }
    
    public DbSet<QueuedDownload> QueuedDownloads { get; set; }
    public DbSet<DownloadStatus> DownloadStatuses { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.Entity<QueuedDownload>().HasKey(d => d.Id);
        modelBuilder.Entity<DownloadStatus>().HasKey(d => d.Id);
    }
}

// 3. State Management Service

public class DownloadStateService
{
    private readonly Dictionary<Guid, DownloadStatus> _activeDownloads = new();
    private readonly Lock _lock = new();
    private readonly ILogger<DownloadStateService> _logger;
    private readonly DownloadDbContext _dbContext;

    public event Action<DownloadStatus>? OnDownloadProgressChanged;
    public event Action? OnDownloadsChanged;

    public DownloadStateService(ILogger<DownloadStateService> logger, DownloadDbContext dbContext)
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

// 4. Download Queue Service

public interface IDownloadQueue
{
    Task<Guid> EnqueueAsync(QueuedDownload download);
    Task<QueuedDownload?> DequeueNextEligibleAsync(CancellationToken cancellationToken);
    Task<int> GetQueuedCountAsync();
    Task RemoveAsync(Guid id);
}

public class DatabaseDownloadQueue : IDownloadQueue
{
    private readonly DownloadDbContext _dbContext;
    private readonly IBandwidthLimiterService _bandwidthLimiter;
    private readonly ILogger<DatabaseDownloadQueue> _logger;

    public DatabaseDownloadQueue(
        DownloadDbContext dbContext,
        IBandwidthLimiterService bandwidthLimiter,
        ILogger<DatabaseDownloadQueue> logger)
    {
        _dbContext = dbContext;
        _bandwidthLimiter = bandwidthLimiter;
        _logger = logger;
    }

    public async Task<Guid> EnqueueAsync(QueuedDownload download)
    {
        if (string.IsNullOrEmpty(download.Domain))
        {
            Uri uri = new Uri(download.Url);
            download.Domain = uri.Host;
        }
        
        _dbContext.QueuedDownloads.Add(download);
        await _dbContext.SaveChangesAsync();
        
        return download.Id;
    }

    public async Task<QueuedDownload?> DequeueNextEligibleAsync(CancellationToken cancellationToken)
    {
        // Get all queued downloads
        var queuedDownloads = await _dbContext.QueuedDownloads
            .OrderByDescending(d => d.Priority)
            .ThenBy(d => d.QueuedAt)
            .ToListAsync(cancellationToken);
            
        foreach (var download in queuedDownloads)
        {
            // Check if bandwidth is available for this domain
            if (!_bandwidthLimiter.IsBandwidthAvailable(download.Domain))
            {
                continue;
            }
            
            // Remove from queue
            _dbContext.QueuedDownloads.Remove(download);
            await _dbContext.SaveChangesAsync(cancellationToken);
            
            return download;
        }
        
        return null;
    }

    public async Task<int> GetQueuedCountAsync()
    {
        return await _dbContext.QueuedDownloads.CountAsync();
    }

    public async Task RemoveAsync(Guid id)
    {
        var download = await _dbContext.QueuedDownloads.FindAsync(id);
        if (download != null)
        {
            _dbContext.QueuedDownloads.Remove(download);
            await _dbContext.SaveChangesAsync();
        }
    }
}

// 5. Bandwidth Limiter Service Interface

public interface IBandwidthLimiterService
{
    bool IsBandwidthAvailable(string domain);
    TimeSpan GetDelayForDomain(string domain);
    void RecordDownload(string domain, long bytes);
}

// 6. Download Service

public interface IDownloadService
{
    Task<Guid> QueueDownloadAsync(DownloadRequest request);
    Task CancelDownloadAsync(Guid id);
    Task PauseDownloadAsync(Guid id);
    Task ResumeDownloadAsync(Guid id);
    Task RetryDownloadAsync(Guid id);
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
        if (status != null && status.State == DownloadState.Paused)
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
        if (status != null && (status.State == DownloadState.Failed || status.State == DownloadState.Canceled))
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