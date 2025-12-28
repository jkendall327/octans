using Microsoft.EntityFrameworkCore;
using Octans.Core.Models;
using Octans.Core.Models.Tagging;

namespace Octans.Core.Tags;

public class TagParentService(ServerDbContext context)
{
    public async Task AddParentAsync(TagModel child, TagModel parent, CancellationToken cancellationToken = default)
    {
        var childTag = await GetOrCreateTagAsync(child, cancellationToken);
        var parentTag = await GetOrCreateTagAsync(parent, cancellationToken);

        if (childTag.Id == parentTag.Id)
        {
            return; // Cannot be own parent
        }

        // Check if relationship already exists
        var exists = await context.TagParents
            .AnyAsync(tp => tp.Child.Id == childTag.Id && tp.Parent.Id == parentTag.Id, cancellationToken);

        if (exists)
        {
            return;
        }

        // Check for cycles.
        // We are adding child -> parent.
        // A cycle exists if parent is already an ancestor of child (i.e. child is a descendant of parent).
        // If we add this link, then parent -> ... -> child -> parent (cycle).
        if (await IsDescendantAsync(childTag.Id, parentTag.Id, cancellationToken))
        {
             throw new InvalidOperationException("Cannot add parent as it would create a cycle.");
        }

        var relationship = new TagParent
        {
            Child = childTag,
            Parent = parentTag,
            Status = 0
        };

        context.TagParents.Add(relationship);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveParentAsync(TagModel child, TagModel parent, CancellationToken cancellationToken = default)
    {
        var relationship = await context.TagParents
            .Include(tp => tp.Child).ThenInclude(t => t.Namespace)
            .Include(tp => tp.Child).ThenInclude(t => t.Subtag)
            .Include(tp => tp.Parent).ThenInclude(t => t.Namespace)
            .Include(tp => tp.Parent).ThenInclude(t => t.Subtag)
            .FirstOrDefaultAsync(tp =>
                tp.Child.Namespace.Value == (child.Namespace ?? string.Empty) &&
                tp.Child.Subtag.Value == child.Subtag &&
                tp.Parent.Namespace.Value == (parent.Namespace ?? string.Empty) &&
                tp.Parent.Subtag.Value == parent.Subtag,
                cancellationToken);

        if (relationship != null)
        {
            context.TagParents.Remove(relationship);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<HashSet<TagModel>> GetDescendantsAsync(TagModel tag, CancellationToken cancellationToken = default)
    {
        var tagEntity = await GetTagAsync(tag, cancellationToken);
        if (tagEntity == null) return [];

        var descendantIds = await GetDescendantIdsAsync([tagEntity.Id], cancellationToken);
        if (descendantIds.Count == 0) return [];

        var result = await context.Tags
            .AsNoTracking()
            .Where(t => descendantIds.Contains(t.Id))
            .Select(t => new TagModel(t.Namespace.Value, t.Subtag.Value))
            .ToListAsync(cancellationToken);

        return result.ToHashSet();
    }

    public async Task<HashSet<int>> GetDescendantIdsAsync(IEnumerable<int> tagIds, CancellationToken cancellationToken = default)
    {
        // Fetch all parent relationships into memory to avoid N+1 queries.
        // This is acceptable if the number of relationships is manageable.
        // If it grows too large, a recursive CTE or stored procedure is required.
        // For now, fetching Id pairs is lightweight.
        var allRelationships = await context.TagParents
            .AsNoTracking()
            .Select(tp => new { ChildId = tp.Child.Id, ParentId = tp.Parent.Id })
            .ToListAsync(cancellationToken);

        // Build adjacency list: Parent -> Children
        var adj = allRelationships
            .GroupBy(x => x.ParentId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ChildId).ToList());

        var descendants = new HashSet<int>();
        var queue = new Queue<int>(tagIds);

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();

            if (adj.TryGetValue(currentId, out var children))
            {
                foreach (var childId in children)
                {
                    if (descendants.Add(childId))
                    {
                        queue.Enqueue(childId);
                    }
                }
            }
        }

        return descendants;
    }

    private async Task<bool> IsDescendantAsync(int childId, int potentialDescendantId, CancellationToken cancellationToken)
    {
        // Check if potentialDescendantId is a descendant of childId
        // This is equivalent to checking if childId is an ancestor of potentialDescendantId

        // We can reuse the memory-based approach
        var descendants = await GetDescendantIdsAsync([childId], cancellationToken);
        return descendants.Contains(potentialDescendantId);
    }

    private async Task<Tag> GetOrCreateTagAsync(TagModel model, CancellationToken cancellationToken)
    {
        var nsStr = model.Namespace ?? string.Empty;
        var subStr = model.Subtag;

        var tag = await context.Tags
            .Include(t => t.Namespace)
            .Include(t => t.Subtag)
            .FirstOrDefaultAsync(t => t.Namespace.Value == nsStr && t.Subtag.Value == subStr, cancellationToken);

        if (tag != null) return tag;

        var ns = await context.Namespaces.FirstOrDefaultAsync(n => n.Value == nsStr, cancellationToken);
        if (ns == null)
        {
            ns = new Namespace { Value = nsStr };
            context.Namespaces.Add(ns);
        }

        var sub = await context.Subtags.FirstOrDefaultAsync(s => s.Value == subStr, cancellationToken);
        if (sub == null)
        {
            sub = new Subtag { Value = subStr };
            context.Subtags.Add(sub);
        }

        tag = new Tag { Namespace = ns, Subtag = sub };
        context.Tags.Add(tag);
        await context.SaveChangesAsync(cancellationToken);

        return tag;
    }

    private async Task<Tag?> GetTagAsync(TagModel model, CancellationToken cancellationToken)
    {
        var nsStr = model.Namespace ?? string.Empty;
        var subStr = model.Subtag;

        return await context.Tags
            .AsNoTracking()
            .Include(t => t.Namespace)
            .Include(t => t.Subtag)
            .FirstOrDefaultAsync(t => t.Namespace.Value == nsStr && t.Subtag.Value == subStr, cancellationToken);
    }
}
