using Microsoft.EntityFrameworkCore;
using Octans.Core.Filesystem;
using Octans.Data.Models;
using Octans.Data.Models.Tagging;

namespace Octans.Core.Querying;

public sealed class FileFinder(ImageStorage imageStorage, ServerDbContext context)
{
    public async Task<List<HashItem>> GetAll()
    {
        return await context.Hashes.ToListAsync();
    }

    public async Task<string?> GetFile(int id)
    {
        var hashItem = await context.FindAsync<HashItem>(id);

        if (hashItem is null)
        {
            return null;
        }

        var hash = ContentHash.FromHashBytes(hashItem.Hash);
        var file = imageStorage.FindOriginal(hash, hashItem.Extension);

        return file?.FullName;
    }

    public async Task<List<HashItem>?> GetFilesByTagQuery(IEnumerable<Tag> tags)
    {
        var found = context.Tags
            .Where(t =>
                tags.Any(tag =>
                tag.Namespace.Value == t.Namespace.Value && tag.Subtag.Value == t.Namespace.Value));

        var query =
            from mapping in context.Mappings
            join tag in found on mapping.Tag equals tag
            select mapping.Hash;

        return await query.ToListAsync();
    }
}
