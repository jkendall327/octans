using Microsoft.Extensions.Logging;
using Octans.Core.Models;
using Octans.Core.Models.Tagging;
using Microsoft.EntityFrameworkCore;

namespace Octans.Core.Importing;

public class DatabaseWriter(ServerDbContext context, ILogger<DatabaseWriter> logger)
{
    public async Task AddItemToDatabase(ImportItem item, HashedBytes hashed)
    {
        var hashItem = new HashItem { Hash = hashed.Bytes };

        context.Hashes.Add(hashItem);

        await AddTags(item, hashItem);

        logger.LogInformation("Persisting item to database");

        await context.SaveChangesAsync();
    }

    private async Task AddTags(ImportItem request, HashItem hashItem)
    {
        var tags = request.Tags;

        if (tags is null || tags.Count == 0)
        {
            return;
        }

        var uniqueTags = tags.Distinct().ToList();

        // 1. Fetch existing Namespaces
        var namespaceValues = uniqueTags.Select(t => t.Namespace ?? string.Empty).Distinct().ToList();
        var existingNamespaces = await context.Namespaces
            .Where(n => namespaceValues.Contains(n.Value))
            .ToDictionaryAsync(n => n.Value, n => n);

        // 2. Fetch existing Subtags
        var subtagValues = uniqueTags.Select(t => t.Subtag).Distinct().ToList();
        var existingSubtags = await context.Subtags
            .Where(s => subtagValues.Contains(s.Value))
            .ToDictionaryAsync(s => s.Value, s => s);

        // 3. Prepare Namespace and Subtag entities (use existing or create new)
        var namespaceEntities = new Dictionary<string, Namespace>();
        foreach (var nsValue in namespaceValues)
        {
            if (existingNamespaces.TryGetValue(nsValue, out var ns))
            {
                namespaceEntities[nsValue] = ns;
            }
            else
            {
                var newNs = new Namespace { Value = nsValue };
                context.Namespaces.Add(newNs);
                namespaceEntities[nsValue] = newNs;
            }
        }

        var subtagEntities = new Dictionary<string, Subtag>();
        foreach (var subValue in subtagValues)
        {
            if (existingSubtags.TryGetValue(subValue, out var sub))
            {
                subtagEntities[subValue] = sub;
            }
            else
            {
                var newSub = new Subtag { Value = subValue };
                context.Subtags.Add(newSub);
                subtagEntities[subValue] = newSub;
            }
        }

        // 4. Fetch existing Tags to avoid duplicates
        // We can only check for existing tags if both namespace and subtag already existed in DB.
        // However, for simplicity and correctness within the current context, we should look up existing tags based on the IDs of the Namespaces/Subtags we found.
        // BUT, we might have just created new Namespace/Subtag entities which don't have IDs yet (if generated on save).
        // So we can check based on values if we load all matching tags.
        // OR we can rely on the fact that if we created a new Namespace or Subtag, the Tag definitely doesn't exist.
        // We only need to check for existing Tags where both Namespace and Subtag already existed.

        var potentialExistingTags = new List<TagModel>();
        foreach (var tag in uniqueTags)
        {
            var nsValue = tag.Namespace ?? string.Empty;
            var subValue = tag.Subtag;

            if (existingNamespaces.ContainsKey(nsValue) && existingSubtags.ContainsKey(subValue))
            {
                potentialExistingTags.Add(tag);
            }
        }

        var existingTagsMap = new Dictionary<(string ns, string sub), Tag>();

        if (potentialExistingTags.Count > 0)
        {
             // We need to fetch tags that match the (Namespace, Subtag) pairs.
             // EF Core doesn't support Where(t => pairs.Contains(..)) easily for composite keys or pairs without client eval or complex predicates.
             // We can fetch tags where Namespace matches ANY of the potential namespaces AND Subtag matches ANY of the potential subtags, then filter in memory.
             var potentialNsIds = potentialExistingTags.Select(t => existingNamespaces[t.Namespace ?? string.Empty].Id).Distinct().ToList();
             var potentialSubIds = potentialExistingTags.Select(t => existingSubtags[t.Subtag].Id).Distinct().ToList();

             var candidateTags = await context.Tags
                 .Include(t => t.Namespace)
                 .Include(t => t.Subtag)
                 .Where(t => potentialNsIds.Contains(t.Namespace.Id) && potentialSubIds.Contains(t.Subtag.Id))
                 .ToListAsync();

             foreach (var t in candidateTags)
             {
                 existingTagsMap[(t.Namespace.Value, t.Subtag.Value)] = t;
             }
        }

        foreach (var tagModel in uniqueTags)
        {
            var nsValue = tagModel.Namespace ?? string.Empty;
            var subValue = tagModel.Subtag;

            if (existingTagsMap.TryGetValue((nsValue, subValue), out var existingTag))
            {
                context.Mappings.Add(new Mapping { Tag = existingTag, Hash = hashItem });
            }
            else
            {
                // Create new tag
                var tagDto = new Tag
                {
                    Namespace = namespaceEntities[nsValue],
                    Subtag = subtagEntities[subValue]
                };
                context.Tags.Add(tagDto);
                context.Mappings.Add(new Mapping { Tag = tagDto, Hash = hashItem });

                // Add to map so we don't try to add it again if the list had duplicates (though we did Distinct())
                existingTagsMap[(nsValue, subValue)] = tagDto;
            }
        }
    }
}
