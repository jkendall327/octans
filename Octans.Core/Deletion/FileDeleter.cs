using Octans.Core;
using Octans.Core.Models;

namespace Octans.Server.Services;

public record DeleteResult(int Id, bool Success, string? Error);
public record DeleteRequest(IEnumerable<int> Ids);
public record DeleteResponse(List<DeleteResult> Results);

public class FileDeleter(SubfolderManager subfolderManager, ServerDbContext context)
{
    public async Task<List<DeleteResult>> ProcessDeletion(IEnumerable<int> request)
    {
        var results = new List<DeleteResult>();

        foreach (var id in request)
        {
            var result = await DeleteFile(id);
            results.Add(result);
        }

        await context.SaveChangesAsync();

        return results;
    }

    private async Task<DeleteResult> DeleteFile(int id)
    {
        var entry = await context.Hashes.FindAsync(id);

        if (entry is null)
        {
            return new(id, false, "Hash not found");
        }

        var hashed = HashedBytes.FromHashed(entry.Hash);

        var file = subfolderManager.GetFilepath(hashed);

        if (file?.Exists == true)
        {
            file.Delete();
        }

        var thumbnail = subfolderManager.GetThumbnail(hashed);

        if (thumbnail?.Exists == true)
        {
            thumbnail.Delete();
        }

        entry.DeletedAt = DateTime.Now;

        return new(id, true, null);
    }
}