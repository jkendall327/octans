using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Octans.Core.Downloaders;
using Octans.Core.Models;

namespace Octans.Core.Downloads;

public interface IDownloadQueue
{
    Task<Guid> EnqueueAsync(QueuedDownload download);
    Task<QueuedDownload?> DequeueNextEligibleAsync(CancellationToken cancellationToken);
    Task<int> GetQueuedCountAsync();
    Task RemoveAsync(Guid id);
}

public class DatabaseDownloadQueue : IDownloadQueue
{
    private readonly ServerDbContext _dbContext;
    private readonly IBandwidthLimiterService _bandwidthLimiter;
    private readonly ILogger<DatabaseDownloadQueue> _logger;

    public DatabaseDownloadQueue(
        ServerDbContext dbContext,
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