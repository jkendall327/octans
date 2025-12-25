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
    public async Task<int> CountAsync(IEnumerable<string> queries, CancellationToken cancellationToken = default)
    {
        var predicates = parser.Parse(queries);

        var plan = planner.OptimiseQuery(predicates);

        var query = converter.Reduce(plan);

        return await searcher.CountAsync(query, cancellationToken);
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
