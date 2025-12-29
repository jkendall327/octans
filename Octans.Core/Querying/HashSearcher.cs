using Microsoft.EntityFrameworkCore;
using Octans.Core.Models;
using Octans.Core.Models.Tagging;
using Octans.Core.Repositories;
using Octans.Core.Tags;

namespace Octans.Core.Querying;

/// <summary>
/// Executes a query plan against the database and returns the relevant hashes.
/// </summary>
public class HashSearcher(ServerDbContext context, TagParentService tagParentService)
{
    public async Task<int> CountAsync(DecomposedQuery request, CancellationToken cancellationToken = default)
    {
        IQueryable<HashItem> query;

        if (request.IsEmpty() || request.SystemPredicates.OfType<EverythingPredicate>().Any())
        {
            query = context.Hashes.AsQueryable();
        }
        else
        {
            var matchingIds = await GetMatchingTagIds(request, cancellationToken);
            query = context.Mappings
                .Where(m => matchingIds.Contains(m.Tag.Id))
                .Select(m => m.Hash)
                .Distinct();
        }

        query = ApplyRepositoryFilter(query, request);

        return await query.CountAsync(cancellationToken);
    }

    public async Task<HashSet<HashItem>> Search(DecomposedQuery request, CancellationToken cancellationToken = default)
    {
        IQueryable<HashItem> query;

        if (request.IsEmpty() || request.SystemPredicates.OfType<EverythingPredicate>().Any())
        {
            query = context.Hashes.AsQueryable();
        }
        else
        {
            var matchingIds = await GetMatchingTagIds(request, cancellationToken);
            query = context.Mappings
                .Where(m => matchingIds.Contains(m.Tag.Id))
                .Include(m => m.Hash)
                .Select(m => m.Hash)
                .Distinct();
        }

        query = ApplyRepositoryFilter(query, request);

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

    private async Task<List<int>> GetMatchingTagIds(DecomposedQuery request, CancellationToken cancellationToken)
    {
        // 1. Get IDs of directly included tags
        var includeIds = new HashSet<int>();
        foreach (var tag in request.TagsToInclude)
        {
            var ns = tag.Namespace ?? string.Empty;
            var sub = tag.Subtag;

            var ids = await context.Tags
                .Where(t => t.Namespace.Value == ns && t.Subtag.Value == sub)
                .Select(t => t.Id)
                .ToListAsync(cancellationToken);

            foreach(var id in ids) includeIds.Add(id);
        }

        // 2. Handle wildcard namespaces
        if (request.WildcardNamespacesToInclude.Any())
        {
            var wildcardIds = await context.Tags
                .Where(t => request.WildcardNamespacesToInclude.Contains(t.Namespace.Value))
                .Select(t => t.Id)
                .ToListAsync(cancellationToken);

            foreach(var id in wildcardIds) includeIds.Add(id);
        }

        // 3. Expand with descendants
        if (includeIds.Any())
        {
            var descendantIds = await tagParentService.GetDescendantIdsAsync(includeIds, cancellationToken);
            includeIds.UnionWith(descendantIds);
        }

        // 4. Handle Excludes
        if (request.TagsToExclude.Any())
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
                 foreach(var id in ids) excludeIds.Add(id);
            }

            includeIds.ExceptWith(excludeIds);
        }

        return includeIds.ToList();
    }
}
