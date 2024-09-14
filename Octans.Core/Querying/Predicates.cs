namespace Octans.Core.Querying;

public interface IPredicate
{
}

public class TagPredicate : IPredicate
{
    public string NamespacePattern { get; set; }
    public string SubtagPattern { get; set; }
    public bool IsExclusive { get; set; }

    public bool IsWildcard()
    {
        return NamespacePattern.Contains(Constants.WILDCARD) || SubtagPattern.Contains(Constants.WILDCARD);
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
    public List<IPredicate> Predicates { get; set; }
}