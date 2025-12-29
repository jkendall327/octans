namespace Octans.Core.Querying;

/// <summary>
/// Converts a rich query plan composed of IPredicates into 'raw' lists of tags/namespaces to include/exclude.
/// </summary>
public class QueryTagConverter
{
    public DecomposedQuery Reduce(QueryPlan plan)
    {
        (var system, var tags, var ors) = plan.Predicates.Partition<SystemPredicate, TagPredicate, OrPredicate>();

        var include = new HashSet<TagModel>();
        var exclude = new HashSet<TagModel>();

        var wellFormed = tags.Where(x => x.IsSpecificTag());

        foreach (var tagPredicate in wellFormed)
        {
            ProcessTagPredicate(tagPredicate, include, exclude);
        }

        return new()
        {
            SystemPredicates = system,
            TagsToInclude = include,
            TagsToExclude = exclude
        };
    }

    private void ProcessPredicate(IPredicate predicate, HashSet<TagModel> includeTags, HashSet<TagModel> excludeTags)
    {
        switch (predicate)
        {
            case TagPredicate tagPredicate:
                ProcessTagPredicate(tagPredicate, includeTags, excludeTags);

                break;
            case OrPredicate orPredicate:
                ProcessOrPredicate(orPredicate, includeTags, excludeTags);

                break;
        }
    }

    private void ProcessTagPredicate(TagPredicate tagPredicate,
        HashSet<TagModel> includeTags,
        HashSet<TagModel> excludeTags)
    {
        if (tagPredicate.IsWildcard())
            return;

        var model = new TagModel(Namespace: tagPredicate.NamespacePattern, Subtag: tagPredicate.SubtagPattern);

        if (tagPredicate.IsExclusive)
        {
            excludeTags.Add(model);
        }
        else
        {
            includeTags.Add(model);
        }
    }

    private void ProcessOrPredicate(OrPredicate orPredicate,
        HashSet<TagModel> includeTags,
        HashSet<TagModel> excludeTags)
    {
        var localIncludeTags = new HashSet<TagModel>();
        var localExcludeTags = new HashSet<TagModel>();

        foreach (var innerPredicate in orPredicate.Predicates)
        {
            ProcessPredicate(innerPredicate, localIncludeTags, localExcludeTags);
        }

        // For OR predicates, we add all local tags to the main sets
        includeTags.UnionWith(localIncludeTags);
        excludeTags.UnionWith(localExcludeTags);
    }
}

public class DecomposedQuery
{
    public List<SystemPredicate> SystemPredicates { get; init; } = [];
    public HashSet<TagModel> TagsToInclude { get; init; } = [];
    public HashSet<TagModel> TagsToExclude { get; init; } = [];

    public HashSet<string> WildcardNamespacesToInclude { get; init; } = [];
    public HashSet<string> WildcardNamespacesToExclude { get; init; } = [];
    public HashSet<string> WildcardSubtagsToInclude { get; init; } = [];
    public HashSet<string> WildcardSubtagsToExclude { get; init; } = [];
    public HashSet<string> WildcardDoublesToInclude { get; init; } = [];
    public HashSet<string> WildcardDoublesToExclude { get; init; } = [];

    public int? Limit { get; init; }
    public int Offset { get; init; }

    public bool IsEmpty()
    {
        var hasContent = TagsToInclude.Any() || WildcardNamespacesToInclude.Any() || WildcardSubtagsToInclude.Any() ||
                         WildcardDoublesToInclude.Any() || SystemPredicates.Any();

        return !hasContent;
    }
}