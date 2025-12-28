using Microsoft.EntityFrameworkCore;
using Octans.Core.Models;
using Octans.Core.Models.Tagging;
using Octans.Core.Tags;

namespace Octans.Core.Querying;

/// <summary>
/// Executes a query plan against the database and returns the relevant hashes.
/// </summary>
public class HashSearcher(ServerDbContext context, TagParentService tagParentService)
{
    public async Task<int> CountAsync(DecomposedQuery request, CancellationToken cancellationToken = default)
    {
        if (request.IsEmpty() || request.SystemPredicates.OfType<EverythingPredicate>().Any())
        {
            return await context.Hashes.CountAsync(cancellationToken);
        }

        var matchingIds = await GetMatchingTagIds(request, cancellationToken);

        var count = await context.Mappings
            .Where(m => matchingIds.Contains(m.Tag.Id))
            .Select(m => m.Hash.Id)
            .Distinct()
            .CountAsync(cancellationToken);

        return count;
    }

    public async Task<HashSet<HashItem>> Search(DecomposedQuery request, CancellationToken cancellationToken = default)
    {
        if (request.IsEmpty() || request.SystemPredicates.OfType<EverythingPredicate>().Any())
        {
            var allHashes = await context.Hashes.ToListAsync(cancellationToken);
            return allHashes.ToHashSet();
        }

        var matchingIds = await GetMatchingTagIds(request, cancellationToken);

        var hashes = await context.Mappings
            .Where(m => matchingIds.Contains(m.Tag.Id))
            .Include(m => m.Hash)
            .Select(m => m.Hash)
            .Distinct()
            .ToListAsync(cancellationToken);

        return hashes.ToHashSet();
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
