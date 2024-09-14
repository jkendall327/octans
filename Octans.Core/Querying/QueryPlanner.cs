using Microsoft.Extensions.Caching.Memory;

namespace Octans.Core.Querying;

/// <summary>
/// Service for optimising a set of generated predicates.
/// Removes duplicates, short-circuits in case of negating predicates, removes redundant predicates, etc.
/// </summary>
public class QueryPlanner
{
    private readonly IMemoryCache _cache;

    public QueryPlanner(IMemoryCache memoryCache)
    {
        _cache = memoryCache;
    }

    public QueryPlan OptimiseQuery(IList<IPredicate> predicates)
    {
        var hashes = predicates
            .Select(p => p.GetHashCode())
            .OrderBy(h => h);
        
        var cacheKey = string.Join("|", hashes);

        if (_cache.TryGetValue(cacheKey, out QueryPlan? cachedPlan) && cachedPlan is not null)
        {
            return cachedPlan;
        }

        var plan = Optimise(predicates);

        var cacheEntryOptions = new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromMinutes(5));

        _cache.Set(cacheKey, plan, cacheEntryOptions);

        return plan;
    }
    
    private string GenerateCacheKey(IEnumerable<IPredicate> predicates)
    {
        return string.Join("|", predicates.Select(p => p.GetHashCode()).OrderBy(h => h));
    }
    
    private QueryPlan Optimise(IEnumerable<IPredicate> predicates)
    {
        // negative ORs are isomorphic to two separate negatives.
        return null;
    }
}

public class QueryPlan
{
    public List<IPredicate> Predicates { get; set; }

    public static QueryPlan NoResults = null;
    public static QueryPlan GetEverything = null;
}