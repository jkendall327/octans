using Microsoft.EntityFrameworkCore;
using Octans.Data.Models;

namespace Octans.Core.Stats;

public sealed class StatsService(ServerDbContext dbContext, StorageService storageService)
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

        // Calculate storage used
        var storageUsed = storageService.GetStorageUsed();

        return new()
        {
            TotalImages = totalImages,
            InboxCount = inboxCount,
            TagCount = tagCount,
            StorageUsed = storageUsed
        };
    }

}
