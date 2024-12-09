using Octans.Core.Models;

namespace Octans.Core.Querying;

public class QueryService
{
    private readonly QueryParser _parser;
    private readonly QueryPlanner _planner;
    private readonly QueryTagConverter _converter;
    private readonly HashSearcher _searcher;

    public QueryService(QueryParser parser, QueryPlanner planner, QueryTagConverter converter, HashSearcher searcher)
    {
        _parser = parser;
        _planner = planner;
        _converter = converter;
        _searcher = searcher;
    }

    public async Task<HashSet<HashItem>> Query(IEnumerable<string> queries)
    {
        var predicates = _parser.Parse(queries);

        var plan = _planner.OptimiseQuery(predicates);

        var query = _converter.Reduce(plan);

        var items = await _searcher.Search(query);

        return items;
    }
}