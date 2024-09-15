using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Octans.Core.Models;

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

    public async Task<HashSet<HashItem>> Search(DecomposedQuery request, CancellationToken token = default)
    {
        /*
         * do we still need to boil down the query plan into raw tags we search for?
         * is it better to do system predicates first?
         * do specific tags before wildcards as they cut down more stuff.
         * don't try to do everything in the database.
         */

        var tags = await _context.Tags
            .Select(t => new TagModel{ Namespace = t.Namespace.Value, Subtag = t.Subtag.Value })
            .ToListAsync(token);

        var matching = tags
            .Join(request.TagsToInclude, 
                s => s.Namespace + ':' + s.Subtag, 
                tm => tm.Namespace + ':' + tm.Subtag, 
                (tag, _) => tag)
            .ToList();

        matching = matching.Except(request.TagsToExclude).ToList();

        var items = await _context.Hashes.ToListAsync(token);
        
        return items.ToHashSet();
    }
}