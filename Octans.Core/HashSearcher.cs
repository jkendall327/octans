using Microsoft.EntityFrameworkCore;
using Octans.Core.Models;
using Octans.Core.Models.Tagging;

namespace Octans.Core;

public class SearchRequest
{
    public List<int> SystemPredicates { get; set; }
    public List<int> OrPredicates { get; set; }
    public List<TagModel> TagsToInclude { get; set; }
    public List<int> TagsToExclude { get; set; }
    public List<int> NamespacesToInclude { get; set; }
    public List<int> NamespacesToExclude { get; set; }
    public List<int> WildcardsToInclude { get; set; }
    public List<int> WildcardsToExclude { get; set; }
}

public class HashSearcher
{
    public const char NAMESPACE_DELIMITER = ':';
    public const char WILDCARD = '*';
    
    private readonly ServerDbContext _context;

    public HashSearcher(ServerDbContext context)
    {
        _context = context;
    }

    public async Task<HashSet<HashItem>> Search(SearchRequest request, CancellationToken token = default)
    {
        
        return new();
    }

    private async Task<List<Namespace>> GetNamespacesFromQueryPortion(string wildcard, CancellationToken token = default)
    {
        if (wildcard == WILDCARD.ToString())
        {
            return await _context.Namespaces.ToListAsync(token);
        }

        var clean = wildcard.Replace(WILDCARD.ToString(), string.Empty);
        
        return await _context.Namespaces.Where(n => n.Value.Contains(clean)).ToListAsync(token);
    }

    private async Task<HashSet<Tag>> GetAutocompleteTagIds(string search, bool exact, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return [];
        }
        
        (var space, var subtag) = SplitTag(search);

        if (string.IsNullOrWhiteSpace(subtag))
        {
            return [];
        }

        if (exact && (space.Contains(WILDCARD) || subtag.Contains(WILDCARD)))
        {
            return [];
        }

        List<Namespace> namespaces;

        if (space.Contains(WILDCARD))
        {
            namespaces = await GetNamespacesFromQueryPortion(space, token);
        }
        else
        {
            var found = await _context.Namespaces.FirstOrDefaultAsync(n => n.Value == space, token);

            // User asked for a specific namespace, but it doesn't exist. Nothing we can do.
            if (found is null)
            {
                return [];
            }

            namespaces = [found];
        }

        var tagsForFoundNamespaces= from t in _context.Tags 
            join ns in namespaces on t.Namespace.Id equals ns.Id 
            select t;
        
        List<Tag> tags = [];

        if (subtag == WILDCARD.ToString())
        {
            // Just get every tag, since the user explicitly searched for '*'.
            if (!namespaces.Any())
            {
                tags = await _context.Tags.ToListAsync(token);
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
        var source = namespaces.Any() ? tagsForFoundNamespaces : _context.Tags;

        var clean = subtag.Replace(WILDCARD.ToString(), string.Empty);
        
        tags = await source
            .Where(t => t.Subtag.Value.Contains(clean))
            .ToListAsync(token);

        return tags.ToHashSet();
    }

    private (string space, string subtag) SplitTag(string tag)
    {
        var split = tag.Split(NAMESPACE_DELIMITER);

        return split.Length switch
        {
            0 => throw new InvalidOperationException("Somehow, splitting a tag resulted in an empty array"),
            1 => (string.Empty, split.First()),
            2 => (split.First(), split.Last()),
            var _ => throw new InvalidOperationException("Splitting a tag resulted in >2 entries"),
        };
    }

    private List<Predicate> ConvertTagListToPredicates(List<string> tags)
    {
        var predicates = new List<Predicate>();
        
        var systemPredicates = tags.Where(s => s.StartsWith("system:")).ToList();

        foreach (var systemPredicate in systemPredicates)
        {
            predicates.Add(new SystemPredicate());
        }
        
        foreach (var tag in tags.Except(systemPredicates))
        {
            var inclusive = tag.StartsWith('-');

            (var space, var subtag) = SplitTag(tag);

            if (tag.Contains(WILDCARD.ToString()))
            {
                if (subtag == WILDCARD.ToString())
                {
                    //tag = namespace
                    //predicate_type = ClientSearch.PREDICATE_TYPE_NAMESPACE
                }
                else
                {
                    // predicate_type = ClientSearch.PREDICATE_TYPE_WILDCARD
                }
            }
            else
            {
                // predicate_type = ClientSearch.PREDICATE_TYPE_TAG
            }
            
            // predicates.append( ClientSearch.Predicate( predicate_type = predicate_type, value = tag, inclusive = inclusive ) )
            predicates.Add(new TagPredicate());
        }
        
        return predicates;
    }
}

public class Predicate
{
}

public class SystemPredicate : Predicate
{
}

public class TagPredicate : Predicate
{
    public string Value { get; set; }
    public bool Inclusive { get; set; }
}