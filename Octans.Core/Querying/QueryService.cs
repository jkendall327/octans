using Octans.Core.Models;

namespace Octans.Core.Querying;

public class QueryService(QueryParser parser, QueryPlanner planner, QueryTagConverter converter, HashSearcher searcher)
{
    public async Task<HashSet<HashItem>> Query(IEnumerable<string> queries)
    {
        var predicates = parser.Parse(queries);

        var plan = planner.OptimiseQuery(predicates);

        var query = converter.Reduce(plan);

        var items = await searcher.Search(query);

        return items;
    }
}