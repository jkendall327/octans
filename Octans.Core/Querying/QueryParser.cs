using System.Text.RegularExpressions;
using Octans.Data.Models;

namespace Octans.Core.Querying;

/// <summary>
/// Parses the query-builder's predicate strings into a semantic query tree. The
/// top-level list is an implicit AND; OR is always represented explicitly.
/// </summary>
internal sealed class QueryParser
{
    public List<IPredicate> Parse(IEnumerable<string> queries)
    {
        var predicates = new List<IPredicate>();
        var errors = new List<QueryError>();

        foreach (var (raw, index) in queries.Select((value, index) => (value, index)))
        {
            try
            {
                predicates.Add(ParsePredicate(raw, index, 0));
            }
            catch (QueryValidationException ex)
            {
                errors.AddRange(ex.Errors);
            }
        }

        if (errors.Count > 0)
        {
            throw new QueryValidationException(errors);
        }

        return predicates;
    }

    private IPredicate ParsePredicate(string raw, int predicateIndex, int sourceOffset)
    {
        var leadingWhitespace = raw.Length - raw.TrimStart().Length;
        var value = Regex.Replace(raw.Trim(), @"\s+", " ");
        sourceOffset += leadingWhitespace;

        if (value.Length == 0)
        {
            throw Error("empty_predicate", "Query predicates cannot be empty.", predicateIndex, sourceOffset, raw.Length);
        }

        if (IsWrapped(value))
        {
            return ParsePredicate(value[1..^1], predicateIndex, sourceOffset + 1);
        }

        var exclusive = value.StartsWith(PredicateConstants.Negation);
        if (exclusive)
        {
            value = value[1..].TrimStart();
            sourceOffset++;
        }

        if (value.StartsWith("or:", StringComparison.OrdinalIgnoreCase))
        {
            if (exclusive)
            {
                throw Error(
                    "unsupported_negation",
                    "Negating an OR group is not supported. Negate its individual alternatives instead.",
                    predicateIndex,
                    sourceOffset - 1,
                    raw.Length);
            }

            return ParseOr(value, predicateIndex, sourceOffset);
        }

        var delimiter = value.IndexOf(PredicateConstants.NamespaceDelimiter, StringComparison.Ordinal);
        var prefix = delimiter < 0 ? string.Empty : value[..delimiter].Trim();
        var query = delimiter < 0 ? value.Trim() : value[(delimiter + 1)..].Trim();

        if (query.Length == 0)
        {
            throw Error("empty_subtag", "A tag or system predicate value is required.", predicateIndex, sourceOffset, value.Length);
        }

        if (prefix.Equals("system", StringComparison.OrdinalIgnoreCase))
        {
            if (exclusive)
            {
                throw Error(
                    "unsupported_negation",
                    "System predicates cannot be negated in query v1.",
                    predicateIndex,
                    sourceOffset - 1,
                    value.Length + 1);
            }

            return ParseSystem(query, predicateIndex, sourceOffset, value.Length);
        }

        var wildcard = Regex.Escape(PredicateConstants.Wildcard.ToString());
        prefix = Regex.Replace(prefix, $"{wildcard}{{2,}}", PredicateConstants.Wildcard.ToString());
        query = Regex.Replace(query, $"{wildcard}{{2,}}", PredicateConstants.Wildcard.ToString());

        return new TagPredicate
        {
            IsExclusive = exclusive,
            NamespacePattern = prefix,
            SubtagPattern = query,
            Source = new(predicateIndex, sourceOffset, value.Length)
        };
    }

    private OrPredicate ParseOr(string value, int predicateIndex, int sourceOffset)
    {
        var body = value[3..].TrimStart();
        var bodyOffset = sourceOffset + value.IndexOf(body, StringComparison.Ordinal);
        var alternatives = SplitAlternatives(body, predicateIndex, bodyOffset);

        if (alternatives.Count < 2)
        {
            throw Error(
                "or_requires_alternatives",
                "An OR group requires at least two alternatives separated by OR.",
                predicateIndex,
                sourceOffset,
                value.Length);
        }

        return new OrPredicate
        {
            Source = new(predicateIndex, sourceOffset, value.Length),
            Predicates = alternatives
                .Select(part => ParsePredicate(part.Value, predicateIndex, part.Start))
                .ToList()
        };
    }

    private static List<QueryPart> SplitAlternatives(string value, int predicateIndex, int sourceOffset)
    {
        var result = new List<QueryPart>();
        var depth = 0;
        var partStart = 0;

        for (var index = 0; index < value.Length; index++)
        {
            depth += value[index] switch
            {
                '(' => 1,
                ')' => -1,
                _ => 0
            };

            if (depth < 0)
            {
                throw Error("unbalanced_parenthesis", "The query contains an unmatched closing parenthesis.", predicateIndex, sourceOffset + index, 1);
            }

            if (depth != 0 || !IsOrSeparator(value, index))
            {
                continue;
            }

            AddPart(result, value, partStart, index, sourceOffset, predicateIndex);
            index += 3;
            partStart = index + 1;
        }

        if (depth != 0)
        {
            throw Error("unbalanced_parenthesis", "The query contains an unmatched opening parenthesis.", predicateIndex, sourceOffset, value.Length);
        }

        AddPart(result, value, partStart, value.Length, sourceOffset, predicateIndex);
        return result;
    }

    private static void AddPart(
        List<QueryPart> result,
        string value,
        int start,
        int end,
        int sourceOffset,
        int predicateIndex)
    {
        var raw = value[start..end];
        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
        {
            throw Error("empty_or_alternative", "OR alternatives cannot be empty.", predicateIndex, sourceOffset + start, Math.Max(1, raw.Length));
        }

        result.Add(new(trimmed, sourceOffset + start + raw.IndexOf(trimmed, StringComparison.Ordinal)));
    }

    private static bool IsOrSeparator(string value, int index) =>
        index + 4 < value.Length &&
        value[index] == ' ' &&
        value.AsSpan(index + 1, 2).Equals("OR", StringComparison.OrdinalIgnoreCase) &&
        value[index + 3] == ' ';

    private static bool IsWrapped(string value)
    {
        if (value.Length < 2 || value[0] != '(' || value[^1] != ')')
        {
            return false;
        }

        var depth = 0;
        for (var index = 0; index < value.Length; index++)
        {
            depth += value[index] switch
            {
                '(' => 1,
                ')' => -1,
                _ => 0
            };

            if (depth == 0 && index < value.Length - 1)
            {
                return false;
            }
        }

        return depth == 0;
    }

    private static SystemPredicate ParseSystem(string query, int predicateIndex, int sourceOffset, int length) =>
        query.ToLowerInvariant() switch
        {
            "everything" => new EverythingPredicate { Source = new(predicateIndex, sourceOffset, length) },
            "inbox" => new RepositoryPredicate { Repository = RepositoryType.Inbox, Source = new(predicateIndex, sourceOffset, length) },
            "archive" => new RepositoryPredicate { Repository = RepositoryType.Archive, Source = new(predicateIndex, sourceOffset, length) },
            "trash" => new RepositoryPredicate { Repository = RepositoryType.Trash, Source = new(predicateIndex, sourceOffset, length) },
            _ => throw Error(
                "unsupported_system_predicate",
                $"System predicate '{query}' is not supported.",
                predicateIndex,
                sourceOffset,
                length)
        };

    private static QueryValidationException Error(
        string code,
        string message,
        int predicateIndex,
        int start,
        int length) => new([new(code, message, predicateIndex, Math.Max(0, start), Math.Max(1, length))]);

    private readonly record struct QueryPart(string Value, int Start);
}
