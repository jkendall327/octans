using System.Security.Cryptography;
using Octans.Core;
using Octans.Core.Models;
using Octans.Core.Models.Tagging;
using Microsoft.EntityFrameworkCore;

namespace Octans.Server;

public class FileFinder
{
    private readonly SubfolderManager _subfolderManager;
    private readonly ServerDbContext _context;

    public FileFinder(SubfolderManager subfolderManager, ServerDbContext context)
    {
        _subfolderManager = subfolderManager;
        _context = context;
    }

    public async Task<string?> GetFile(int id)
    {
        var hashItem = await _context.FindAsync<HashItem>(id);

        if (hashItem is null)
        {
            return null;
        }

        var hashed = new HashedBytes(hashItem.Hash, ItemType.File);
        
        var subfolder = _subfolderManager.GetSubfolder(hashed);

        return Directory
            .EnumerateFiles(subfolder.AbsolutePath)
            .SingleOrDefault(x => x.Contains(hashed.Hexadecimal));
    }

    public async Task<List<HashItem>?> GetFilesByTagQuery(IEnumerable<Tag> tags)
    {
        var found = _context.Tags
            .Where(t => 
                tags.Any(tag =>
                tag.Namespace.Value == t.Namespace.Value && tag.Subtag.Value == t.Namespace.Value));

        var query = 
            from mapping in _context.Mappings
            join tag in found on mapping.Tag equals tag
            select mapping.Hash;
        
        return await query.ToListAsync();
    }
}