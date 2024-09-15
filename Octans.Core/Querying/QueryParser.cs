using System.Text.RegularExpressions;

namespace Octans.Core.Querying;

/// <summary>
/// Converts raw strings represent components of a query into strongly-typed predicates.
/// </summary>
public class QueryParser
{
    public List<IPredicate> Parse(IEnumerable<string> queries)
    {
        var cleaned = queries.Select(StringToRawQuery);
        
        var predicates = ConvertRawQueriesToPredicates(cleaned);
        
        return predicates;
    }

    private List<IPredicate> ConvertRawQueriesToPredicates(IEnumerable<RawQuery> cleaned)
    {
        var predicates = new List<IPredicate>();
        
        foreach (var query in cleaned)
        {
            var result = query.Prefix switch
            {
                "system" => ParseSystemPredicate(query),
                "or" => ParseOrPredicate(query),
                var _ => ParseTagPredicate(query)
            };
           
            predicates.Add(result);
        }

        return predicates;
    }

    private IPredicate ParseSystemPredicate(RawQuery query)
    {
        if (query.Query is "everything")
        {
            return new EverythingPredicate();
        }
        
        throw new NotImplementedException();
    }

    private IPredicate ParseOrPredicate(RawQuery query)
    {
        var components = query.Query.Split(Constants.OR_SEPARATOR);
        
        var raw = components.Select(StringToRawQuery);
        
        var parsed = ConvertRawQueriesToPredicates(raw);

        return new OrPredicate
        {
            Predicates = parsed,
        };
    }

    private IPredicate ParseTagPredicate(RawQuery query)
    {
        var predicate = new TagPredicate
        {
            IsExclusive = query.Exclusive,
            NamespacePattern = query.Prefix,
            SubtagPattern = query.Query
        };

        return predicate;
    }
    
    private RawQuery StringToRawQuery(string raw)
    {
        // Remove leading/trailing whitespace and collapse multiple consecutive whitespace.
        var cleaned = Regex.Replace(raw.Trim(), @"\s+", " ");

        // TODO: this should also replace multiple consecutive wildcards with just one.
        
        var exclusive = cleaned.StartsWith(Constants.QUERY_NEGATION);
        
        var split = cleaned.Split(Constants.NAMESPACE_DELIMITER);

        (var prefix, var query) = split.Length switch
        {
            0 => throw new InvalidOperationException("Somehow, splitting a raw query resulted in an empty array"),
            1 => (string.Empty, split.First()),
            2 => (split.First(), split.Last()),
            // OR queries will have multiple namespace delimiters.
            // E.g. 'or:character:bayonetta OR character:samus aran'
            // The below line will result in ["or"], ["character:bayonetta OR character:samus aran"].
            var _ => (split.First(), string.Join(':', split.Skip(1))),
        };

        prefix = prefix.Replace("-", string.Empty);
        
        return new()
        {
            Exclusive = exclusive,
            Prefix = prefix.Trim(),
            Query = query.Trim()
        };
    }
}

/// <summary>
/// A raw string from the user that has undergone whitespace removal and colon-splitting,
/// but hasn't yet been parsed as a system predicate, tag predicate, etc. 
/// </summary>
public class RawQuery
{
    public string Prefix { get; set; }
    public string Query { get; set; }
    public bool Exclusive { get; set; }
}