using Octans.Core.Models;

namespace Octans.Core.Querying;

public class SearchRequest
{
    public List<string> SystemPredicates { get; set; }
    public List<string> OrPredicates { get; set; }
    public List<string> TagsToInclude { get; set; }
    public List<string> TagsToExclude { get; set; }
    public List<string> NamespacesToInclude { get; set; }
    public List<string> NamespacesToExclude { get; set; }
    public List<string> WildcardsToInclude { get; set; }
    public List<string> WildcardsToExclude { get; set; }
}

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

    public async Task<HashSet<HashItem>> Search(SearchRequest request, CancellationToken token = default)
    {
        throw new NotImplementedException();
    }
}