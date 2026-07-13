using System.Runtime.CompilerServices;
using Octans.Data.Models;

namespace Octans.Core.Querying;

public interface IQueryService
{
    Task<FileQueryServicePage> QueryAsync(FileQueryRequest request, CancellationToken cancellationToken = default);
    Task<int> CountAsync(IEnumerable<string> queries, CancellationToken cancellationToken = default);
    IAsyncEnumerable<HashItem> Query(IEnumerable<string> queries, CancellationToken cancellationToken = default);
}

internal sealed class QueryService(QueryParser parser, QueryPlanner planner, QueryTagConverter converter, HashSearcher searcher) : IQueryService
{
    public async Task<FileQueryServicePage> QueryAsync(
        FileQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Offset < 0)
        {
            throw new QueryValidationException([new(
                "invalid_offset",
                "Query offset cannot be negative.",
                -1,
                0,
                1)]);
        }

        if (request.Limit is < 1 or > 500)
        {
            throw new QueryValidationException([new(
                "invalid_limit",
                "Query limit must be between 1 and 500.",
                -1,
                0,
                1)]);
        }

        var predicates = parser.Parse(request.Predicates);
        var plan = planner.OptimiseQuery(predicates);
        var total = await searcher.CountAsync(converter.Reduce(plan), cancellationToken);
        var items = await searcher.Search(converter.Reduce(plan, request.Limit, request.Offset), cancellationToken);

        return new(
            items.OrderBy(item => item.Id).ToList(),
            total,
            request.Offset,
            request.Limit);
    }

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
