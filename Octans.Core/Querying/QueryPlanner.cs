namespace Octans.Core.Querying;

/// <summary>
/// Service for optimising a set of generated predicates.
/// Removes duplicates, short-circuits in case of negating predicates, removes redundant predicates, etc.
/// </summary>
public class QueryPlanner
{
    public void OptimiseQuery(IEnumerable<IPredicate> predicates)
    {
        
    }
}

public class QueryPlan
{
    public List<IPredicate> Predicates { get; set; }

    public static QueryPlan NoResults = null;
    public static QueryPlan GetEverything = null;
}