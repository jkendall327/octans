using NLua;

namespace Octans.Core.Downloaders;

public class Downloader
{
    private readonly Dictionary<string, object> _metadata;
    
    private readonly LuaFunction _matchUrl;
    private readonly LuaFunction _classifyUrl;
    private readonly LuaFunction _parseHtml;
    private readonly LuaFunction _generateGalleryUrl;
    private readonly LuaFunction _processApiQuery;

    public Downloader(Dictionary<string, Lua> functions, Dictionary<string, object> metadata)
    {
        _metadata = metadata;
        
        if (functions.TryGetValue("classifier", out var classifier))
        {
            _matchUrl = GetLuaFunction(classifier, "match_url");
            _classifyUrl = GetLuaFunction(classifier, "classify_url");
        }

        if (functions.TryGetValue("parser", out var parser))
        {
            _parseHtml = GetLuaFunction(parser, "parse_html");
        }

        if (functions.TryGetValue("gug", out var gug))
        {
            _generateGalleryUrl = GetLuaFunction(gug, "generate_url");
        }

        if (functions.TryGetValue("api", out var api))
        {
            _processApiQuery = GetLuaFunction(api, "process_query");
        }
    }
    
    private LuaFunction GetLuaFunction(Lua lua, string functionName)
    {
        return lua[functionName] as LuaFunction ?? throw new InvalidOperationException($"{functionName} function not found");
    }

    public bool MatchesUrl(string url)
    {
        var res = _matchUrl.Call(url)?.FirstOrDefault();

        return res is true;
    }

    public object? ClassifyUrl(string url)
    {
        return _classifyUrl.Call(url)?.FirstOrDefault();
    }

    public List<string> ParseHtml(string htmlContent)
    {
        var result = _parseHtml.Call(htmlContent)?.FirstOrDefault() as LuaTable;

        return result?.Values.Cast<string>().ToList() ?? new List<string>();
    }

    public string GenerateGalleryUrl(string input, int page)
    {
        var result = _generateGalleryUrl.Call(input, page)?.FirstOrDefault() as string;

        return result ?? throw new InvalidOperationException("Failed to generate gallery URL");
    }

    public string ProcessApiQuery(string query)
    {
        var result = _processApiQuery.Call(query)?.FirstOrDefault() as LuaTable;

        return string.Empty;
    }
}