using System.Diagnostics.CodeAnalysis;

namespace Octans.Core.Querying;

[SuppressMessage("Design", "CA1040:Avoid empty interfaces", Justification = "Required at compile-time")]
public interface IPredicate;

public class TagPredicate : IPredicate
{
    public required string NamespacePattern { get; set; }
    public required string SubtagPattern { get; set; }
    public bool IsExclusive { get; set; }

    public bool IsWildcard()
    {
        return NamespacePattern.Contains(PredicateConstants.Wildcard) || SubtagPattern.Contains(PredicateConstants.Wildcard);
    }

    public bool IsSpecificTag() => !IsWildcard();
}

public abstract class SystemPredicate : IPredicate
{
}

public class FilesizePredicate : SystemPredicate
{

}

public class EverythingPredicate : SystemPredicate
{

}

public class OrPredicate : IPredicate
{
    public List<IPredicate> Predicates { get; init; } = [];
}