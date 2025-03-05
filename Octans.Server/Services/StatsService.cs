using Microsoft.EntityFrameworkCore;
using Octans.Core.Communication;
using Octans.Core.Models;

namespace Octans.Server.Services;

public class StatsService(ServerDbContext dbContext)
{
    public async Task<HomeStats> GetHomeStats()
    {
        // Get total images (non-deleted)
        var totalImages = await dbContext.Hashes
            .CountAsync(h => h.DeletedAt == null);

        // Get inbox count (faked for now)
        var inboxCount = 0;

        // Get unique tag count
        var tagCount = await dbContext.Tags.CountAsync();

        // For storage used, we'll just return a placeholder for now
        var storageUsed = "0 GB";

        return new HomeStats
        {
            TotalImages = totalImages,
            InboxCount = inboxCount,
            TagCount = tagCount,
            StorageUsed = storageUsed
        };
    }
}
