using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Octans.Core.Filesystem;
using Octans.Data.Models;

namespace Octans.Core.Importing;

public class ReimportChecker(
    ServerDbContext context,
    ImageStorage imageStorage,
    FilesystemWriter filesystemWriter,
    ILogger<ReimportChecker> logger)
{
    public async Task<ImportItemResult?> CheckIfPreviouslyDeleted(
        ContentHash hash,
        ImageMetadata metadata,
        bool allowReimportDeleted,
        byte[] bytes)
    {
        var existingHash = await context.Hashes
            .FirstOrDefaultAsync(h => h.Hash == hash.Bytes);

        if (existingHash == null) return null;

        if (existingHash.IsDeleted() && !allowReimportDeleted)
        {
            return new()
            {
                Ok = false,
                Message = "Image was previously deleted and reimport is not allowed"
            };
        }

        existingHash.DeletedAt = null;
        existingHash.Extension ??= metadata.Extension;
        existingHash.ContentType ??= metadata.ContentType;
        await context.SaveChangesAsync();

        logger.LogInformation("Reactivated previously deleted hash: {HashId}", existingHash.Id);

        var existingFile = imageStorage.FindOriginal(hash, existingHash.Extension);
        if (existingFile is null)
        {
            logger.LogInformation("Restoring content for previously deleted hash: {HashId}", existingHash.Id);
            await filesystemWriter.WriteOriginal(hash, metadata, bytes);
        }

        return new()
        {
            Ok = true,
            Message = "Previously deleted image has been reimported"
        };
    }
}
