using System.Runtime.CompilerServices;
using Octans.Core.Models;

namespace Octans.Core.Querying;

public interface IQueryService
{
    Task<int> CountAsync(IEnumerable<string> queries, CancellationToken cancellationToken = default);
    IAsyncEnumerable<HashItem> Query(IEnumerable<string> queries, CancellationToken cancellationToken = default);
}

public class QueryService(QueryParser parser, QueryPlanner planner, QueryTagConverter converter, HashSearcher searcher) : IQueryService
{
    // TODO: World's worst form of optimisation, just doing the entire operation
    public async Task<int> CountAsync(IEnumerable<string> queries, CancellationToken cancellationToken = default)
    {
        var predicates = parser.Parse(queries);

        var plan = planner.OptimiseQuery(predicates);

        var query = converter.Reduce(plan);

        var items = await searcher.Search(query, cancellationToken);    
        
        return items.Count;
    }

    public async IAsyncEnumerable<HashItem> Query(IEnumerable<string> queries,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var predicates = parser.Parse(queries);

        var plan = planner.OptimiseQuery(predicates);

        var query = converter.Reduce(plan);

        var items = await searcher.Search(query, cancellationToken);

        foreach (var item in items)
        {
            yield return item;
        }
    }
}