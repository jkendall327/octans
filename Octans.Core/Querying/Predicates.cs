using System.Diagnostics.CodeAnalysis;
using Octans.Data.Models;

namespace Octans.Core.Querying;

[SuppressMessage("Design", "CA1040:Avoid empty interfaces", Justification = "Required at compile-time")]
internal interface IPredicate;

internal readonly record struct QuerySource(int PredicateIndex, int Start, int Length);

internal sealed class TagPredicate : IPredicate
{
    public required string NamespacePattern { get; set; }
    public required string SubtagPattern { get; set; }
    public bool IsExclusive { get; set; }
    public QuerySource Source { get; init; }

    public bool IsWildcard()
    {
        return NamespacePattern.Contains(PredicateConstants.Wildcard, StringComparison.OrdinalIgnoreCase) ||
               SubtagPattern.Contains(PredicateConstants.Wildcard, StringComparison.OrdinalIgnoreCase);
    }

    public bool IsSpecificTag() => !IsWildcard();
}

internal abstract class SystemPredicate : IPredicate
{
    public QuerySource Source { get; init; }
}

internal sealed class FilesizePredicate : SystemPredicate
{
}

internal sealed class EverythingPredicate : SystemPredicate
{
}

internal sealed class RepositoryPredicate : SystemPredicate
{
    public required RepositoryType Repository { get; init; }
}

internal sealed class OrPredicate : IPredicate
{
    public List<IPredicate> Predicates { get; init; } = [];
    public QuerySource Source { get; init; }
}
