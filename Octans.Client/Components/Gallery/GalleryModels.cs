namespace Octans.Client.Components.Pages;

public enum QueryKind
{
    Normal,
    System
}

public record QueryParameter(string Raw, QueryKind Kind);