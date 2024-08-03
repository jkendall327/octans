using System.Security.Cryptography;
using HydrusReplacement.Core;
using HydrusReplacement.Core.Models;
using HydrusReplacement.Core.Models.Tagging;
using Microsoft.EntityFrameworkCore;

namespace HydrusReplacement.Server;

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
        var hash = await _context.FindAsync<HashItem>(id);

        if (hash is null)
        {
            return null;
        }
                
        var hex = Convert.ToHexString(hash.Hash);
                
        var subfolder = _subfolderManager.GetSubfolder(hash.Hash);

        return Directory
            .EnumerateFiles(subfolder.AbsolutePath)
            .SingleOrDefault(x => x.Contains(hex));
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