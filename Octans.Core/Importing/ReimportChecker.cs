using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Octans.Core.Models;

namespace Octans.Core.Importing;

public class ReimportChecker(
    ServerDbContext context,
    SubfolderManager subfolderManager,
    FilesystemWriter filesystemWriter,
    ILogger<ReimportChecker> logger)
{
    public async Task<ImportItemResult?> CheckIfPreviouslyDeleted(
        HashedBytes hashed,
        bool allowReimportDeleted,
        byte[] bytes)
    {
        var existingHash = await context.Hashes
            .FirstOrDefaultAsync(h => h.Hash == hashed.Bytes);

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
        await context.SaveChangesAsync();

        logger.LogInformation("Reactivated previously deleted hash: {HashId}", existingHash.Id);

        var existingFile = subfolderManager.GetFilepath(hashed);
        if (existingFile is null)
        {
            logger.LogInformation("Restoring content for previously deleted hash: {HashId}", existingHash.Id);
            await filesystemWriter.CopyBytesToSubfolder(hashed, bytes);
        }

        return new()
        {
            Ok = true,
            Message = "Previously deleted image has been reimported"
        };
    }
}