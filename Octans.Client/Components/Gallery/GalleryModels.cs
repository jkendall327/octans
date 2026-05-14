namespace Octans.Client.Components.Gallery;

public enum QueryKind
{
    Normal,
    System
}

public record QueryParameter(string Raw, QueryKind Kind);

public sealed class SortOptions
{

}

public sealed class CollectionOptions
{

}