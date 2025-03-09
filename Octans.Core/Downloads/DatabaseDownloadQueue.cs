using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Octans.Core.Downloaders;
using Octans.Core.Models;

namespace Octans.Core.Downloads;

[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix")]
public interface IDownloadQueue
{
    Task<Guid> EnqueueAsync(QueuedDownload download);
    Task<QueuedDownload?> DequeueNextEligibleAsync(CancellationToken cancellationToken);
    Task<int> GetQueuedCountAsync();
    Task RemoveAsync(Guid id);
}

[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix")]
public class DatabaseDownloadQueue(
    IDbContextFactory<ServerDbContext> contextFactory,
    IBandwidthLimiter bandwidthLimiter,
    ILogger<DatabaseDownloadQueue> logger) : IDownloadQueue
{
    public async Task<Guid> EnqueueAsync(QueuedDownload download)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["DownloadId"] = download.Id,
            ["Url"] = download.Url
        });

        logger.LogInformation("Enqueuing download with priority {Priority}", download.Priority);

        if (string.IsNullOrEmpty(download.Domain))
        {
            var uri = new Uri(download.Url);
            download.Domain = uri.Host;
            logger.LogDebug("Extracted domain {Domain} from URL", download.Domain);
        }

        await using var db = await contextFactory.CreateDbContextAsync();

        db.QueuedDownloads.Add(download);
        await db.SaveChangesAsync();

        logger.LogDebug("Download successfully added to queue");
        return download.Id;
    }

    public async Task<QueuedDownload?> DequeueNextEligibleAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Attempting to dequeue next eligible download");

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        // Get all queued downloads
        var queuedDownloads = await db.QueuedDownloads
            .OrderByDescending(d => d.Priority)
            .ThenBy(d => d.QueuedAt)
            .ToListAsync(cancellationToken);

        logger.LogDebug("Found {Count} downloads in queue", queuedDownloads.Count);

        foreach (var download in queuedDownloads)
        {
            using var scope = logger.BeginScope(new Dictionary<string, object?>
            {
                ["DownloadId"] = download.Id,
                ["Domain"] = download.Domain
            });

            // Check if bandwidth is available for this domain
            if (!bandwidthLimiter.IsBandwidthAvailable(download.Domain))
            {
                logger.LogDebug("Skipping download due to bandwidth limitations for domain {Domain}", download.Domain);
                continue;
            }

            logger.LogInformation("Dequeuing download from {Domain}", download.Domain);

            // Remove from queue
            db.QueuedDownloads.Remove(download);
            await db.SaveChangesAsync(cancellationToken);

            return download;
        }

        logger.LogDebug("No eligible downloads found in queue");
        return null;
    }

    public async Task<int> GetQueuedCountAsync()
    {
        logger.LogDebug("Getting queued download count");

        await using var db = await contextFactory.CreateDbContextAsync();
        var count = await db.QueuedDownloads.CountAsync();

        logger.LogDebug("Current queue size: {Count} downloads", count);
        return count;
    }

    public async Task RemoveAsync(Guid id)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object?> { ["DownloadId"] = id });
        logger.LogInformation("Removing download from queue");

        await using var db = await contextFactory.CreateDbContextAsync();

        var download = await db.QueuedDownloads.FindAsync(id);
        if (download != null)
        {
            logger.LogDebug("Found download in queue, removing");
            db.QueuedDownloads.Remove(download);
            await db.SaveChangesAsync();
        }
        else
        {
            logger.LogDebug("Download not found in queue");
        }
    }
}
