namespace Octans.Core;

public record TagModel(string Namespace, string Subtag)
{
    public bool IsFullyQualified => !HasNoNamespace && !HasWildcardNamespace && !HasWildcardSubtag;

    public bool HasWildcardNamespace =>
        !string.IsNullOrWhiteSpace(Namespace) &&
        Namespace.Contains(PredicateConstants.Wildcard, StringComparison.OrdinalIgnoreCase);

    public bool HasNoNamespace => string.IsNullOrWhiteSpace(Namespace);
    public bool HasWildcardSubtag => Subtag.Contains(PredicateConstants.Wildcard, StringComparison.OrdinalIgnoreCase);

    public bool IsFullWildcard =>
        Namespace == PredicateConstants.Wildcard.ToString() && Subtag == PredicateConstants.Wildcard.ToString();

    public static TagModel WithoutNamespace(string subtag) => new(string.Empty, subtag);
}