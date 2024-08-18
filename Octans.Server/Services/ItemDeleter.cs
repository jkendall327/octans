using Octans.Core;
using Octans.Core.Models;

namespace Octans.Server.Services;

public class ItemDeleter
{
    private readonly SubfolderManager _subfolderManager;
    private readonly ServerDbContext _context;

    public ItemDeleter(SubfolderManager subfolderManager, ServerDbContext context)
    {
        _subfolderManager = subfolderManager;
        _context = context;
    }

    public async Task<List<DeleteResult>> ProcessDeletion(DeleteRequest request)
    {
        var results = new List<DeleteResult>();

        foreach (var item in request.Items)
        {
            var result = await DeleteFile(item);
            results.Add(result);
        }

        await _context.SaveChangesAsync();

        return results;
    }
    
    private async Task<DeleteResult> DeleteFile(DeleteItem item)
    {
        var hashItem = await _context.Hashes.FindAsync(item.Id);
        
        if (hashItem is null)
        {
            return new(item.Id, false, "Hash not found");
        }

        var hashed = HashedBytes.FromHashed(hashItem.Hash);
        var fileInfo = _subfolderManager.GetFilepath(hashed);

        if (fileInfo?.Exists == true)
        {
            fileInfo.Delete();
        }

        hashItem.DeletedAt = DateTime.Now;

        return new(item.Id, true, null);
    }
}