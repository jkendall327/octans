using Microsoft.EntityFrameworkCore;
using Octans.Core.Models;
using Octans.Core.Models.Tagging;
using Octans.Core.Tags;

namespace Octans.Core.Querying;

/// <summary>
/// Provides suggestions on relevant tags given text, e.g. for autocomplete dropdowns.
/// </summary>
public class QuerySuggestionFinder(ServerDbContext context)
{
    public async Task<HashSet<Tag>> GetAutocompleteTagIds(string search, bool exact, CancellationToken token = default)
    {
        return await context.Tags.ToHashSetAsync(cancellationToken: token);

        // TODO: reimplement this.
        
        // if (string.IsNullOrWhiteSpace(search))
        // {
        //     return [];
        // }
        //
        // (var space, var subtag) = splitter.SplitTag(search);
        //
        // if (string.IsNullOrWhiteSpace(subtag))
        // {
        //     return [];
        // }
        //
        // if (exact && (space.Contains(PredicateConstants.Wildcard, StringComparison.OrdinalIgnoreCase) ||
        //               subtag.Contains(PredicateConstants.Wildcard, StringComparison.OrdinalIgnoreCase)))
        // {
        //     return [];
        // }
        //
        // List<Namespace> namespaces;
        //
        // if (space.Contains(PredicateConstants.Wildcard, StringComparison.OrdinalIgnoreCase))
        // {
        //     namespaces = await GetNamespacesFromQueryPortion(space, token);
        // }
        // else
        // {
        //     var found = await context.Namespaces.FirstOrDefaultAsync(n => n.Value == space, token);
        //
        //     // User asked for a specific namespace, but it doesn't exist. Nothing we can do.
        //     if (found is null)
        //     {
        //         return [];
        //     }
        //
        //     namespaces = [found];
        // }
        //
        // var tagsForFoundNamespaces =
        //     from t in context.Tags join ns in namespaces on t.Namespace.Id equals ns.Id select t;
        //
        // List<Tag> tags = [];
        //
        // if (subtag == PredicateConstants.Wildcard.ToString())
        // {
        //     // Just get every tag, since the user explicitly searched for '*'.
        //     if (!namespaces.Any())
        //     {
        //         tags = await context.Tags.ToListAsync(token);
        //     }
        //
        //     // Get all tags for all the wildcard-expanded namespaces.
        //     else
        //     {
        //         tags = await tagsForFoundNamespaces.ToListAsync(token);
        //     }
        // }
        //
        // if (tags.Any())
        // {
        //     return tags.ToHashSet();
        // }
        //
        // // If the user specified 1+ namespaces, only consider tags in those spaces.
        // // Otherwise, search everything.
        // var source = namespaces.Any() ? tagsForFoundNamespaces : context.Tags;
        //
        // var clean = subtag.Replace(PredicateConstants.Wildcard.ToString(), string.Empty, StringComparison.Ordinal);
        //
        // tags = await source
        //     .Where(t => t.Subtag.Value.Contains(clean))
        //     .ToListAsync(token);
        //
        // return tags.ToHashSet();
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