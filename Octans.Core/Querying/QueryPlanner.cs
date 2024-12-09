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

    private QueryPlan Optimise(IList<IPredicate> predicates)
    {
        if (!predicates.Any())
        {
            return QueryPlan.NoResults;
        }

        (var system, var tags, var ors) = predicates.Partition<SystemPredicate, TagPredicate, OrPredicate>();

        if (system.OfType<EverythingPredicate>().Any())
        {
            return QueryPlan.GetEverything;
        }

        // negative ORs are isomorphic to two separate negatives.

        // we don't want to remove specific tags in favour of wildcard tags
        // as specific tags are probably way cheaper to search for and cut down the results massively.

        // see the query optimisation doc for guidelines here

        return new()
        {
            Predicates = predicates.ToList()
        };
    }
}

public class QueryPlan
{
    public required List<IPredicate> Predicates { get; init; }

    public static readonly QueryPlan NoResults = new() { Predicates = [] };
    public static readonly QueryPlan GetEverything = new() { Predicates = [new EverythingPredicate()] };
}