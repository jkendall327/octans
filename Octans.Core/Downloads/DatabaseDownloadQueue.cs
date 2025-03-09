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

public class DatabaseDownloadQueue(
    IDbContextFactory<ServerDbContext> contextFactory,
    IBandwidthLimiter bandwidthLimiter,
    ILogger<DatabaseDownloadQueue> logger) : IDownloadQueue
{
    public async Task<Guid> EnqueueAsync(QueuedDownload download)
    {
        if (string.IsNullOrEmpty(download.Domain))
        {
            var uri = new Uri(download.Url);
            download.Domain = uri.Host;
        }
        
        await using var db = await contextFactory.CreateDbContextAsync();
        
        db.QueuedDownloads.Add(download);
        await db.SaveChangesAsync();
        
        return download.Id;
    }

    public async Task<QueuedDownload?> DequeueNextEligibleAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        // Get all queued downloads
        var queuedDownloads = await db.QueuedDownloads
            .OrderByDescending(d => d.Priority)
            .ThenBy(d => d.QueuedAt)
            .ToListAsync(cancellationToken);
            
        foreach (var download in queuedDownloads)
        {
            // Check if bandwidth is available for this domain
            if (!bandwidthLimiter.IsBandwidthAvailable(download.Domain))
            {
                continue;
            }
            
            // Remove from queue
            db.QueuedDownloads.Remove(download);
            await db.SaveChangesAsync(cancellationToken);
            
            return download;
        }
        
        return null;
    }

    public async Task<int> GetQueuedCountAsync()
    {
        await using var db = await contextFactory.CreateDbContextAsync();

        return await db.QueuedDownloads.CountAsync();
    }

    public async Task RemoveAsync(Guid id)
    {
        await using var db = await contextFactory.CreateDbContextAsync();

        var download = await db.QueuedDownloads.FindAsync(id);
        if (download != null)
        {
            db.QueuedDownloads.Remove(download);
            await db.SaveChangesAsync();
        }
    }
}