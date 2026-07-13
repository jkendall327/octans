namespace Octans.Core.Querying;

/// <summary>
/// Service for optimising a set of generated predicates.
/// Removes duplicates, short-circuits in case of negating predicates, removes redundant predicates, etc.
/// </summary>
internal sealed class QueryPlanner
{
    public QueryPlan OptimiseQuery(IList<IPredicate> predicates)
    {
        return new()
        {
            Predicates = predicates.DistinctBy(GetStructuralKey).ToList()
        };
    }

    private static string GetStructuralKey(IPredicate predicate) => predicate switch
    {
        TagPredicate tag => $"tag:{tag.IsExclusive}:{tag.NamespacePattern}:{tag.SubtagPattern}",
        RepositoryPredicate repository => $"repository:{repository.Repository}",
        EverythingPredicate => "everything",
        OrPredicate or => $"or:{string.Join('|', or.Predicates.Select(GetStructuralKey))}",
        _ => predicate.GetType().FullName ?? predicate.GetType().Name
    };
}

internal sealed class QueryPlan
{
    public required List<IPredicate> Predicates { get; init; }

}
