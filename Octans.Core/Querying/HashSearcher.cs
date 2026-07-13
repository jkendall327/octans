using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Octans.Core.Tags;
using Octans.Data.Models;

namespace Octans.Core.Querying;

/// <summary>
/// Executes a query plan against the database and returns the relevant hashes.
/// </summary>
internal sealed class HashSearcher(ServerDbContext context, TagParentService tagParentService)
{
    public async Task<int> CountAsync(DecomposedQuery request, CancellationToken cancellationToken = default)
    {
        var query = await BuildQuery(request, cancellationToken);

        return await query.CountAsync(cancellationToken);
    }

    public async Task<HashSet<HashItem>> Search(DecomposedQuery request, CancellationToken cancellationToken = default)
    {
        var query = await BuildQuery(request, cancellationToken);

        if (request.Offset > 0)
        {
            query = query.OrderBy(h => h.Id).Skip(request.Offset);
        }

        if (request.Limit.HasValue)
        {
            if (request.Offset == 0)
            {
                query = query.OrderBy(h => h.Id);
            }
            query = query.Take(request.Limit.Value);
        }

        var hashes = await query.ToListAsync(cancellationToken);

        return hashes.ToHashSet();
    }

    private async Task<IQueryable<HashItem>> BuildQuery(DecomposedQuery request, CancellationToken cancellationToken)
    {
        var query = context.Hashes
            .Where(h => h.DeletedAt == null)
            .AsQueryable();

        if (request.Plan is not null)
        {
            return await BuildSemanticQuery(query, request.Plan, cancellationToken);
        }

        if (ShouldStartFromAllHashes(request))
        {
            return ApplyRepositoryFilter(query, request);
        }

        var requiredTagIdGroups = await GetRequiredTagIdGroups(request, cancellationToken);

        if (requiredTagIdGroups.Count == 0 || requiredTagIdGroups.Any(g => g.Count == 0))
        {
            query = query.Where(_ => false);
        }
        else
        {
            foreach (var tagIds in requiredTagIdGroups)
            {
                var requiredTagIds = tagIds.ToList();

                query = query.Where(hash => context.Mappings
                    .Any(mapping => mapping.Hash.Id == hash.Id && requiredTagIds.Contains(mapping.Tag.Id)));
            }
        }

        return ApplyRepositoryFilter(query, request);
    }

    private async Task<IQueryable<HashItem>> BuildSemanticQuery(
        IQueryable<HashItem> query,
        QueryPlan plan,
        CancellationToken cancellationToken)
    {
        var tagIds = new Dictionary<TagPredicate, IReadOnlyList<int>>();
        foreach (var tag in EnumerateTagPredicates(plan.Predicates).Distinct())
        {
            tagIds[tag] = await GetMatchingTagIds(tag, cancellationToken);
        }

        if (!ContainsTrashPredicate(plan.Predicates))
        {
            query = query.Where(h => h.RepositoryId != (int)RepositoryType.Trash);
        }

        foreach (var predicate in plan.Predicates)
        {
            query = query.Where(BuildExpression(predicate, tagIds));
        }

        return query;
    }

    private async Task<IReadOnlyList<int>> GetMatchingTagIds(
        TagPredicate predicate,
        CancellationToken cancellationToken)
    {
        var namespacePattern = ToLikePattern(predicate.NamespacePattern);
        var subtagPattern = ToLikePattern(predicate.SubtagPattern);

        var ids = await context.Tags
            .Where(t => EF.Functions.Like(t.Namespace.Value, namespacePattern, "\\") &&
                        EF.Functions.Like(t.Subtag.Value, subtagPattern, "\\"))
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
        {
            return ids;
        }

        var expanded = ids.ToHashSet();
        expanded.UnionWith(await tagParentService.GetDescendantIdsAsync(expanded, cancellationToken));
        return expanded.ToList();
    }

    private Expression<Func<HashItem, bool>> BuildExpression(
        IPredicate predicate,
        IReadOnlyDictionary<TagPredicate, IReadOnlyList<int>> tagIds) => predicate switch
    {
        EverythingPredicate => hash => true,
        RepositoryPredicate repository => hash => hash.RepositoryId == (int)repository.Repository,
        TagPredicate tag => BuildTagExpression(tag, tagIds[tag]),
        OrPredicate or => or.Predicates
            .Select(child => BuildExpression(child, tagIds))
            .Aggregate(OrElse),
        _ => throw new InvalidOperationException($"Predicate type '{predicate.GetType().Name}' cannot be executed.")
    };

    private Expression<Func<HashItem, bool>> BuildTagExpression(TagPredicate predicate, IReadOnlyList<int> ids)
    {
        Expression<Func<HashItem, bool>> match = hash => context.Mappings
            .Any(mapping => mapping.Hash.Id == hash.Id && ids.Contains(mapping.Tag.Id));

        return predicate.IsExclusive ? Not(match) : match;
    }

    private static Expression<Func<HashItem, bool>> OrElse(
        Expression<Func<HashItem, bool>> left,
        Expression<Func<HashItem, bool>> right)
    {
        var parameter = left.Parameters[0];
        var rightBody = new ReplaceParameterVisitor(right.Parameters[0], parameter).Visit(right.Body)!;
        return Expression.Lambda<Func<HashItem, bool>>(Expression.OrElse(left.Body, rightBody), parameter);
    }

    private static Expression<Func<HashItem, bool>> Not(Expression<Func<HashItem, bool>> expression) =>
        Expression.Lambda<Func<HashItem, bool>>(Expression.Not(expression.Body), expression.Parameters);

    private static IEnumerable<TagPredicate> EnumerateTagPredicates(IEnumerable<IPredicate> predicates)
    {
        foreach (var predicate in predicates)
        {
            if (predicate is TagPredicate tag)
            {
                yield return tag;
            }

            if (predicate is not OrPredicate or)
            {
                continue;
            }

            foreach (var child in EnumerateTagPredicates(or.Predicates))
            {
                yield return child;
            }
        }
    }

    private static bool ContainsTrashPredicate(IEnumerable<IPredicate> predicates) => predicates.Any(predicate =>
        predicate is RepositoryPredicate { Repository: RepositoryType.Trash } ||
        predicate is OrPredicate or && ContainsTrashPredicate(or.Predicates));

    private static string ToLikePattern(string pattern) => pattern
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal)
        .Replace(PredicateConstants.Wildcard.ToString(), "%", StringComparison.Ordinal);

    private static IQueryable<HashItem> ApplyRepositoryFilter(IQueryable<HashItem> query, DecomposedQuery request)
    {
        if (request.RepositoryFilters.Count > 0)
        {
            // If explicit repository filters are present (system:inbox, system:archive, system:trash), use them.
            // Using OR logic if multiple are present (though unlikely to happen with simple parsing logic yet).
            // Actually, usually these are mutually exclusive, but 'OR' implies we could select multiple.
            // Let's assume if multiple are provided, we want ANY of them.
            var repoIds = request.RepositoryFilters.Select(r => (int)r).ToList();
            query = query.Where(h => repoIds.Contains(h.RepositoryId));
        }
        else
        {
            // Default behavior: Show everything EXCEPT Trash.
            query = query.Where(h => h.RepositoryId != (int)RepositoryType.Trash);
        }

        return query;
    }

    private static bool ShouldStartFromAllHashes(DecomposedQuery request)
    {
        if (request.IsEmpty() || request.SystemPredicates.OfType<EverythingPredicate>().Any())
        {
            return true;
        }

        return !request.TagsToInclude.Any()
               && !request.TagsToExclude.Any()
               && !request.WildcardNamespacesToInclude.Any()
               && !request.WildcardNamespacesToExclude.Any()
               && !request.WildcardSubtagsToInclude.Any()
               && !request.WildcardSubtagsToExclude.Any()
               && !request.WildcardDoublesToInclude.Any()
               && !request.WildcardDoublesToExclude.Any()
               && request.RepositoryFilters.Any();
    }

    private async Task<List<HashSet<int>>> GetRequiredTagIdGroups(DecomposedQuery request, CancellationToken cancellationToken)
    {
        var includeGroups = new List<HashSet<int>>();

        foreach (var tag in request.TagsToInclude)
        {
            var ns = tag.Namespace ?? string.Empty;
            var sub = tag.Subtag;

            var includeIds = await context.Tags
                .Where(t => t.Namespace.Value == ns && t.Subtag.Value == sub)
                .Select(t => t.Id)
                .ToListAsync(cancellationToken);

            var expandedIds = includeIds.ToHashSet();

            if (expandedIds.Any())
            {
                var descendantIds = await tagParentService.GetDescendantIdsAsync(expandedIds, cancellationToken);
                expandedIds.UnionWith(descendantIds);
            }

            includeGroups.Add(expandedIds);
        }

        foreach (var @namespace in request.WildcardNamespacesToInclude)
        {
            var includeIds = await context.Tags
                .Where(t => t.Namespace.Value == @namespace)
                .Select(t => t.Id)
                .ToListAsync(cancellationToken);

            includeGroups.Add(includeIds.ToHashSet());
        }

        if (request.TagsToExclude.Any())
        {
            var excludeIds = await GetExcludedTagIds(request, cancellationToken);

            foreach (var includeIds in includeGroups)
            {
                includeIds.ExceptWith(excludeIds);
            }
        }

        return includeGroups;
    }

    private async Task<HashSet<int>> GetExcludedTagIds(DecomposedQuery request, CancellationToken cancellationToken)
    {
        var excludeIds = new HashSet<int>();

        foreach (var tag in request.TagsToExclude)
        {
            var ns = tag.Namespace ?? string.Empty;
            var sub = tag.Subtag;
            var ids = await context.Tags
                .Where(t => t.Namespace.Value == ns && t.Subtag.Value == sub)
                .Select(t => t.Id)
                .ToListAsync(cancellationToken);

            foreach (var id in ids)
            {
                excludeIds.Add(id);
            }
        }

        return excludeIds;
    }

    private sealed class ReplaceParameterVisitor(ParameterExpression source, ParameterExpression target) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == source ? target : node;
    }
}
