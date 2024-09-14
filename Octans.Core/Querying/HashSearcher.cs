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

    public async Task<HashSet<HashItem>> Search(QueryPlan request, CancellationToken token = default)
    {
        throw new NotImplementedException();
    }
}