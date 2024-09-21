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
        if (request.IsEmpty())
        {
            var allHashes = await _context.Hashes.ToListAsync(cancellationToken);
            return allHashes.ToHashSet();
        }
        
        var tags = _context.Tags
            .Include(tag => tag.Namespace)
            .Include(tag => tag.Subtag);

        var toInclude = request.TagsToInclude.Select(ToTagDto).ToList();
        var toExclude = request.TagsToExclude.Select(ToTagDto).ToList();

        var all = await tags.AsNoTracking().ToListAsync(cancellationToken);
        
        var matching = all
            .Join(toInclude,
                s => s.Namespace.Value + ":" + s.Subtag.Value,
                s => s.Namespace.Value + ":" + s.Subtag.Value,
                (s, t) => s)
            .ToList();

        if (request.WildcardNamespacesToInclude.Any())
        {
            var spaces = await _context.Namespaces.Join(request.WildcardNamespacesToInclude,
                    s => s.Value,
                    t => t,
                    (s, t) => s)
                .ToListAsync(cancellationToken);

            var namespaceTags = all
                .Join(spaces, t => t.Namespace.Id, n => n.Id, (s, t) => s)
                .ToList();
            
            matching.AddRange(namespaceTags);
        }
        
        matching = matching.Except(toExclude).ToList();

        var allMappings = await _context.Mappings
            .Include(m => m.Hash)
            .Include(mapping => mapping.Tag)
            .ToListAsync(cancellationToken);

        allMappings = allMappings
            .Join(matching, m => m.Tag.Id, t => t.Id, (m, t) => m)
            .ToList();

        var hashes = allMappings.Select(x => x.Hash).ToHashSet();
        
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