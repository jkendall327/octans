using Microsoft.EntityFrameworkCore;
using Octans.Core.Models;
using Octans.Core.Models.Tagging;
using Octans.Core.Tags;

namespace Octans.Core.Querying;

/// <summary>
/// Provides suggestions on relevant tags given text, e.g. for autocomplete dropdowns.
/// </summary>
public class QuerySuggestionFinder(ServerDbContext context, TagSplitter splitter)
{
    public async Task<HashSet<Tag>> GetAutocompleteTagIds(string search, bool exact, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return [];
        }

        (var space, var subtag) = splitter.SplitTag(search);

        if (string.IsNullOrWhiteSpace(subtag))
        {
            return [];
        }

        if (exact && (space.Contains(PredicateConstants.Wildcard, StringComparison.OrdinalIgnoreCase) ||
                      subtag.Contains(PredicateConstants.Wildcard, StringComparison.OrdinalIgnoreCase)))
        {
            return [];
        }

        List<Namespace> namespaces;

        if (space.Contains(PredicateConstants.Wildcard, StringComparison.OrdinalIgnoreCase))
        {
            namespaces = await GetNamespacesFromQueryPortion(space, token);
        }
        else
        {
            if (string.IsNullOrEmpty(space))
            {
                namespaces = [];
            }
            else
            {
                // Allow partial match for namespace
                namespaces = await context.Namespaces
                    .Where(n => n.Value.Contains(space))
                    .ToListAsync(token);
            }
        }

        var namespaceIds = namespaces.Select(n => n.Id).ToList();

        IQueryable<Tag> tagsForFoundNamespaces;
        
        if (namespaceIds.Count > 0)
        {
            tagsForFoundNamespaces = context.Tags.Where(t => namespaceIds.Contains(t.Namespace.Id));
        }
        else
        {
            // If we have a namespace search term but found no matching namespaces,
            // then there can be no matching tags.
            // However, if space was empty, we fall back to searching all tags (handled below).

            if (!string.IsNullOrEmpty(space))
            {
                return [];
            }

            // If space was empty, we don't use tagsForFoundNamespaces anyway unless wildcards logic below messes it up.
            // But let's define it as empty to be safe, though it shouldn't be reached in that case
            // because of logic later "var source = namespaces.Any() ? ... : context.Tags"
            tagsForFoundNamespaces = context.Tags.Where(t => false);
        }

        List<Tag> tags = [];

        if (subtag == PredicateConstants.Wildcard.ToString())
        {
            // Just get every tag, since the user explicitly searched for '*'.
            // Logic check: if namespaces.Any() is false, it means either:
            // 1. space was empty (so search all tags)
            // 2. space was not empty but no namespace matched (we returned [] above)

            if (!namespaces.Any())
            {
                tags = await context.Tags.ToListAsync(token);
            }

            // Get all tags for all the wildcard-expanded namespaces.
            else
            {
                tags = await tagsForFoundNamespaces.ToListAsync(token);
            }
        }

        if (tags.Any())
        {
            return tags.ToHashSet();
        }

        // If the user specified 1+ namespaces, only consider tags in those spaces.
        // Otherwise, search everything.
        var source = namespaces.Any() ? tagsForFoundNamespaces : context.Tags;

        var clean = subtag.Replace(PredicateConstants.Wildcard.ToString(), string.Empty, StringComparison.Ordinal);

        tags = await source
            .Where(t => t.Subtag.Value.Contains(clean))
            .ToListAsync(token);

        return tags.ToHashSet();
    }

    private async Task<List<Namespace>> GetNamespacesFromQueryPortion(string wildcard,
        CancellationToken token = default)
    {
        if (wildcard == PredicateConstants.Wildcard.ToString())
        {
            return await context.Namespaces.ToListAsync(token);
        }

        var clean = wildcard.Replace(PredicateConstants.Wildcard.ToString(), string.Empty, StringComparison.Ordinal);

        return await context
            .Namespaces
            .Where(n => n.Value.Contains(clean))
            .ToListAsync(token);
    }
}
