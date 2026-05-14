using Microsoft.EntityFrameworkCore;
using Octans.Data.Models;

namespace Octans.Core.Tags;

public class TagService(ServerDbContext context) : ITagService
{
    public async Task<List<TagModel>> GetTagsForHashAsync(string hashHex)
    {
        var bytes = Convert.FromHexString(hashHex);

        var query =
            from mapping in context.Mappings
            where mapping.Hash.Hash == bytes
            select mapping.Tag;

        var tags = await query
            .AsNoTracking()
            .Select(t => new TagModel(t.Namespace.Value, t.Subtag.Value))
            .ToListAsync();

        return tags;
    }
}
