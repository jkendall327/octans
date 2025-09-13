using Microsoft.EntityFrameworkCore;
using Octans.Core.Models;
using Octans.Core.Models.Tagging;

namespace Octans.Core.Tags;

public record ResolvedTag(TagModel Tag, TagModel Display);

public class TagSiblingService(ServerDbContext context)
{
    public async Task<IReadOnlyCollection<ResolvedTag>> Resolve(IEnumerable<TagModel> tags)
    {
        var tagList = tags.ToList();
        if (!tagList.Any())
        {
            return Array.Empty<ResolvedTag>();
        }

        const char delimiter = '\u0001';

        var keys = tagList
            .Select(t => $"{t.Namespace ?? string.Empty}{delimiter}{t.Subtag}")
            .ToList();

        var entities = await context.Tags
            .Include(t => t.Namespace)
            .Include(t => t.Subtag)
            .Where(t => keys.Contains(t.Namespace.Value + delimiter + t.Subtag.Value))
            .ToListAsync();

        if (!entities.Any())
        {
            return tagList.Select(t => new ResolvedTag(t, t)).ToList();
        }

        var siblingEntities = await context.TagSiblings
            .Include(s => s.NonIdeal).ThenInclude(t => t.Namespace)
            .Include(s => s.NonIdeal).ThenInclude(t => t.Subtag)
            .Include(s => s.Ideal).ThenInclude(t => t.Namespace)
            .Include(s => s.Ideal).ThenInclude(t => t.Subtag)
            .Where(s => entities.Select(e => e.Id).Contains(s.NonIdeal.Id))
            .ToListAsync();

        var results = new List<ResolvedTag>();

        foreach (var model in tagList)
        {
            var ns = model.Namespace ?? string.Empty;
            var entity = entities.FirstOrDefault(t => t.Namespace.Value == ns && t.Subtag.Value == model.Subtag);
            if (entity == null)
            {
                results.Add(new(model, model));
                continue;
            }

            var sibling = siblingEntities.FirstOrDefault(s => s.NonIdeal.Id == entity.Id);
            if (sibling == null)
            {
                results.Add(new(model, model));
                continue;
            }

            var display = new TagModel(
                sibling.Ideal.Namespace.Value,
                sibling.Ideal.Subtag.Value);
            results.Add(new(model, display));
        }

        return results;
    }
}
