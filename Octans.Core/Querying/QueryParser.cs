using System.Text.RegularExpressions;

namespace Octans.Core;

public class QueryParser
{
    public Task<SearchRequest> Parse(IEnumerable<string> query)
    {
        var searchRequest = new SearchRequest();

        foreach (var se in query)
        {
            if (se is "whatever")
            {
                searchRequest.TagsToInclude.Add(se);
            }
        }

        return Task.FromResult(searchRequest);
    }

    private SystemPredicate ParseSystemPredicate(string query)
    {
        return new FilesizePredicate();
    }

    private OrPredicate ParseOrPredicate(string query)
    {
        return new OrPredicate();
    }

    private TagPredicate ParseTagPredicate(string space, string subtag)
    {
        var exclusive = space.StartsWith('-');

        var predicate = new TagPredicate
        {
            IsExclusive = exclusive,
            NamespacePattern = space,
            SubtagPattern = subtag
        };

        return predicate;
    }
    
    private List<IPredicate> ConvertRawQueriesToPredicates(List<string> queries)
    {
        var predicates = new List<IPredicate>();

        var cleaned = queries
            .Select(s => s.Trim())
            .Select(s => Regex.Replace(s, @"\s+", " "));

        var split = cleaned.Select(SplitTag);
        
       foreach ((var space, var subtag) in split)
       {
           IPredicate result = space switch
           {
               "system" => ParseSystemPredicate(subtag),
               "or" => ParseOrPredicate(subtag),
               var _ => ParseTagPredicate(space, subtag)
           };
           
           predicates.Add(result);
       }
        
        return predicates;
    }
    
    public const char NAMESPACE_DELIMITER = ':';

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
}