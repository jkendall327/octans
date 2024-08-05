using HydrusReplacement.Core;
using HydrusReplacement.Core.Models;

namespace HydrusReplacement.Server.Services;

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

        // TODO: this is hashing a hash!
        var hashed = new HashedBytes(hashItem.Hash, ItemType.File);
        var fileInfo = _subfolderManager.GetFilepath(hashed);

        if (fileInfo?.Exists == true)
        {
            fileInfo.Delete();
        }

        hashItem.DeletedAt = DateTime.Now;

        return new(item.Id, true, null);
    }
}