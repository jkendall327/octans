using System.Text.RegularExpressions;

namespace Octans.Core.Querying;

public class QueryParser
{
    public List<IPredicate> Parse(IEnumerable<string> queries)
    {
        var predicates = new List<IPredicate>();

        var cleaned = queries.Select(CleanAndInitialParse);
        
        foreach (var query in cleaned)
        {
            if (query.Prefix is "or")
            {
                var results = ParseOrPredicate(query);
                predicates.AddRange(results);
                continue;
            }
            
            IPredicate result = query.Query switch
            {
                "system" => ParseSystemPredicate(query),
                var _ => ParseTagPredicate(query)
            };
           
            predicates.Add(result);
        }
        
        return predicates;
    }
    
    private SystemPredicate ParseSystemPredicate(RawQuery query)
    {
        throw new NotImplementedException();
        return new FilesizePredicate();
    }

    private IEnumerable<IPredicate> ParseOrPredicate(RawQuery query)
    {
        var components = query.Query.Split("OR");
        var raw = components.Select(CleanAndInitialParse);
        
        // run each of these recursively through the parser...

        if (query.Exclusive)
        {
            // return all of the components as separate exclusive predicates as that is simpler and equivalent.
            // no, do this later in a dedicated service for simplifying/reducing query plans.
        }
        
        throw new NotImplementedException();
        return [new OrPredicate()];
    }

    private TagPredicate ParseTagPredicate(RawQuery query)
    {
        var predicate = new TagPredicate
        {
            IsExclusive = query.Exclusive,
            NamespacePattern = query.Prefix,
            SubtagPattern = query.Query
        };

        return predicate;
    }
    
    public const char NAMESPACE_DELIMITER = ':';

    private RawQuery CleanAndInitialParse(string tag)
    {
        var cleaned = tag.Trim();
        cleaned = Regex.Replace(cleaned, @"\s+", " ");

        var exclusive = cleaned.StartsWith('-');
        
        var split = cleaned.Split(NAMESPACE_DELIMITER);

        (var ns, var st) = split.Length switch
        {
            0 => throw new InvalidOperationException("Somehow, splitting a tag resulted in an empty array"),
            1 => (string.Empty, split.First()),
            2 => (split.First(), split.Last()),
            // OR queries will have multiple namespace delimiters.
            // E.g. 'or:character:bayonetta OR character:samus aran'
            // The below line will result in ["or"], ["character:bayonetta OR character:samus aran"].
            var _ => (split.First(), string.Join(':', split.Skip(1))),
        };

        return new()
        {
            Exclusive = exclusive,
            Prefix = ns,
            Query = st
        };
    }
}

/// <summary>
/// A raw string from the user that has undergone whitespace removal and colon-splitting,
/// but hasn't yet been parsed as a system predicate, tag predicate, etc. 
/// </summary>
public class RawQuery
{
    public string Query { get; set; }
    public string Prefix { get; set; }
    public bool Exclusive { get; set; }
}