using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Octans.Core.Models;
using Octans.Core.Models.Tagging;

namespace Octans.Core.Querying;

/// <summary>
/// Executes a query plan against the database and returns the relevant hashes.
/// </summary>
public class HashSearcher
{
    private readonly ServerDbContext _context;

    public HashSearcher(ServerDbContext context)
    {
        _context = context;
    }

    public async Task<HashSet<HashItem>> Search(DecomposedQuery request, CancellationToken cancellationToken = default)
    {
        var tags = _context.Tags
            .Include(tag => tag.Namespace)
            .Include(tag => tag.Subtag);

        var toInclude = request.TagsToInclude.Select(ToTagDto).ToList();
        var toExclude = request.TagsToExclude.Select(ToTagDto).ToList();

        var matching = await tags
            .Join(toInclude,
                s => s.Namespace.Value + ":" + s.Subtag.Value,
                s => s.Namespace.Value + ":" + s.Subtag.Value,
                (s, t) => s)
            .ToListAsync(cancellationToken);
        
        matching = matching.Except(toExclude).ToList();

        var mappings = await _context.Mappings
            .Include(m => m.Hash)
            .Join(matching, m => m.Tag, t => t, (m, t) => m)
            .ToListAsync(cancellationToken);

        var hashes = mappings.Select(x => x.Hash).ToHashSet();
        
        return hashes;
    }

    private Tag ToTagDto(TagModel s)
    {
        return new()
        {
            Namespace = new()
            {
                Value = s.Namespace ?? string.Empty
            },
            Subtag = new()
            {
                Value = s.Subtag
            }
        };
    }
}