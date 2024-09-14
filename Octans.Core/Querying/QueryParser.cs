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
    
    private List<IPredicate> ConvertRawQueriesToPredicates(List<string> queries)
    {
        var predicates = new List<IPredicate>();
        
        var systemPredicates = queries.Where(s => s.StartsWith("system:")).ToList();

        foreach (var systemPredicate in systemPredicates)
        {
            predicates.Add(new FilesizePredicate());
        }
        
        var orPredicates = queries.Where(s => s.StartsWith("or:")).ToList();
        
        foreach (var orPredicate in orPredicates)
        {
            predicates.Add(new OrPredicate());
        }
        
        foreach (var tag in queries.Except(systemPredicates))
        {
            var clean = tag.Trim();
            clean = Regex.Replace(clean, @"\s+", " ");
            
            var exclusive = clean.StartsWith('-');

            (var space, var subtag) = SplitTag(clean);

            var predicate = new TagPredicate
            {
                IsExclusive = exclusive,
                NamespacePattern = space,
                SubtagPattern = subtag
            };
            
            predicates.Add(predicate);
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