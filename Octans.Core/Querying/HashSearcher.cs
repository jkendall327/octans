using Microsoft.EntityFrameworkCore;
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
        // if (request == QueryPlan.NoResults)
        // {
        //     return new();
        // }
        //
        // if (request == QueryPlan.GetEverything)
        // {
        //     var everything = await _context.Hashes.ToListAsync(token);
        //     return everything.ToHashSet();
        // }
        
        /*
         * do we still need to boil down the query plan into raw tags we search for?
         * is it better to do system predicates first?
         * do specific tags before wildcards as they cut down more stuff.
         * don't try to do everything in the database.
         */

        throw new NotImplementedException();
    }
}