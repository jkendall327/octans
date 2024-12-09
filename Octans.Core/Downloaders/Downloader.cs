using NLua;

namespace Octans.Core.Downloaders;

public class DownloaderMetadata
{
    public string Name { get; set; } = string.Empty;
    public string Creator { get; set; } = string.Empty;
    public Version Version { get; set; } = new(0, 0);
    public string Homepage { get; set; } = string.Empty;

    public List<string> SupportedOperations { get; } = new();
}

public enum DownloaderUrlClassification
{
    Post,
    Gallery,
    Unknown
}

public sealed class Downloader : IDisposable
{
    public DownloaderMetadata Metadata { get; }

    private readonly LuaFunction _matchUrl;
    private readonly LuaFunction _classifyUrl;
    private readonly LuaFunction _parseHtml;
    private readonly LuaFunction? _generateGalleryUrl;
    private readonly LuaFunction? _processApiQuery;

    public Downloader(Dictionary<string, Lua> functions, DownloaderMetadata metadata)
    {
        Metadata = metadata;

        var classifier = functions["classifier"];

        _matchUrl = GetLuaFunction(classifier, "match_url");
        _classifyUrl = GetLuaFunction(classifier, "classify_url");
        _parseHtml = GetLuaFunction(functions["parser"], "parse_html");

        Metadata.SupportedOperations.AddRange(["match_url", "classify_url", "parse_html"]);

        if (functions.TryGetValue("gug", out var gug))
        {
            _generateGalleryUrl = GetLuaFunction(gug, "generate_url");
            Metadata.SupportedOperations.Add("generate_url");
        }

        if (functions.TryGetValue("api", out var api))
        {
            _processApiQuery = GetLuaFunction(api, "process_query");
            Metadata.SupportedOperations.Add("process_query");
        }
    }

    private LuaFunction GetLuaFunction(Lua lua, string functionName)
    {
        return lua[functionName] as LuaFunction ?? throw new InvalidOperationException($"{functionName} not found in Lua blob");
    }

    public bool MatchesUrl(string url)
    {
        var res = _matchUrl.Call(url)?.FirstOrDefault();

        return res is true;
    }

    // function classify_url(url) -> "Post" || "Gallery"
    public DownloaderUrlClassification ClassifyUrl(string url)
    {
        var raw = _classifyUrl.Call(url)?.FirstOrDefault();

        if (raw is not string s) return DownloaderUrlClassification.Unknown;

        return s.ToLower() switch
        {
            "post" => DownloaderUrlClassification.Post,
            "gallery" => DownloaderUrlClassification.Gallery,
            var _ => DownloaderUrlClassification.Unknown
        };
    }

    // function parse_html(html_content) -> string[]
    public List<string> ParseHtml(string htmlContent)
    {
        var result = _parseHtml.Call(htmlContent)?.FirstOrDefault() as LuaTable;

        return result?.Values.Cast<string>().ToList() ?? new List<string>();
    }

    public string GenerateGalleryUrl(string input, int page)
    {
        if (_generateGalleryUrl is null)
        {
            throw new InvalidOperationException("No GUG provided for downloader");
        }

        var result = _generateGalleryUrl.Call(input, page)?.FirstOrDefault() as string;

        return result ?? throw new InvalidOperationException("Failed to generate gallery URL");
    }

    public string ProcessApiQuery(string query)
    {
        if (_processApiQuery is null)
        {
            throw new InvalidOperationException("No API component provided for downloader");
        }

        var result = _processApiQuery.Call(query)?.FirstOrDefault() as LuaTable;

        return string.Empty;
    }

    public void Dispose()
    {
        _matchUrl.Dispose();
        _classifyUrl.Dispose();
        _parseHtml.Dispose();
        _generateGalleryUrl?.Dispose();
        _processApiQuery?.Dispose();
    }

    public bool MatchesUrl(Uri url)
    {
        throw new NotImplementedException();
    }

    public DownloaderUrlClassification ClassifyUrl(Uri url)
    {
        throw new NotImplementedException();
    }
}