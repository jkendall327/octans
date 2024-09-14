namespace Octans.Core;

public static class UriExtensions
{
    public static bool IsWebUrl(this Uri uri)
    {
        return uri.Scheme.ToLower() is "http" or "https";
    }
}