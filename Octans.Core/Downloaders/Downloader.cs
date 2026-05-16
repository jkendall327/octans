namespace Octans.Core.Downloaders;

public class DownloaderMetadata
{
    public string Name { get; set; } = string.Empty;
    public string Creator { get; set; } = string.Empty;
    public Version Version { get; set; } = new(0, 0);
    public string Homepage { get; set; } = string.Empty;

    public List<string> SupportedOperations { get; } = [];
}

public enum DownloaderUrlClassification
{
    Post,
    Gallery,
    Unknown
}

public sealed class Downloader : IDisposable
{
    private const int MaxReturnedUrls = 500;
    private const int MaxStringLength = 4096;
    private const int MaxApiParameters = 100;

    public DownloaderMetadata Metadata { get; }

    private readonly DownloaderLuaFunction _matchUrl;
    private readonly DownloaderLuaFunction _classifyUrl;
    private readonly DownloaderLuaFunction _parseHtml;
    private readonly DownloaderLuaFunction? _generateGalleryUrl;
    private readonly DownloaderLuaFunction? _processApiQuery;
    private readonly List<DownloaderLuaContext> _luaContexts;

    internal Downloader(Dictionary<string, DownloaderLuaContext> functions, DownloaderMetadata metadata)
    {
        Metadata = metadata;

        _luaContexts = functions.Values.ToList();

        var classifier = functions["classifier"];

        _matchUrl = GetLuaFunction(classifier, "match_url", "match_url");
        _classifyUrl = GetLuaFunction(classifier, "classify_url", "classify_url");
        _parseHtml = GetLuaFunction(functions["parser"], "parse_html", "parse_html");

        Metadata.SupportedOperations.AddRange(["match_url", "classify_url", "parse_html"]);

        if (functions.TryGetValue("gug", out var gug))
        {
            _generateGalleryUrl = GetLuaFunction(gug, "generate_url", "generate_url");
            Metadata.SupportedOperations.Add("generate_url");
        }

        if (functions.TryGetValue("api", out var api))
        {
            _processApiQuery = GetLuaFunction(api, "process_query", "process_query");
            Metadata.SupportedOperations.Add("process_query");
        }
    }

    private static DownloaderLuaFunction GetLuaFunction(
        DownloaderLuaContext lua,
        string functionName,
        string operation) =>
        new(lua, lua.GetFunction(functionName), operation);

    public bool MatchesUrl(Uri url)
    {
        var res = _matchUrl.Call(url.AbsoluteUri).FirstOrDefault();

        return res is bool b
            ? b
            : throw new DownloaderContractException("match_url must return a boolean.");
    }

    // function classify_url(url) -> "Post" || "Gallery"
    public DownloaderUrlClassification ClassifyUrl(Uri url)
    {
        var raw = _classifyUrl.Call(url.AbsoluteUri).FirstOrDefault();

        if (raw is not string s)
        {
            throw new DownloaderContractException("classify_url must return a string.");
        }

        return s.ToLowerInvariant() switch
        {
            "post" => DownloaderUrlClassification.Post,
            "gallery" => DownloaderUrlClassification.Gallery,
            _ => throw new DownloaderContractException("classify_url must return 'post' or 'gallery'.")
        };
    }

    // function parse_html(html_content) -> string[]
    public List<string> ParseHtml(string htmlContent)
    {
        var result = _parseHtml.Call(htmlContent).FirstOrDefault();

        return ReadStringTable(result, "parse_html", MaxReturnedUrls);
    }

    public string GenerateGalleryUrl(string input, int page)
    {
        if (_generateGalleryUrl is null)
        {
            throw new InvalidOperationException("No GUG provided for downloader");
        }

        var result = _generateGalleryUrl.Call(input, page).FirstOrDefault() as string;

        return ValidateNonEmptyString(result, "generate_url return value");
    }

    public string ProcessApiQuery(string query)
    {
        if (_processApiQuery is null)
        {
            throw new InvalidOperationException("No API component provided for downloader");
        }

        if (_processApiQuery.Call(query).FirstOrDefault() is not NLua.LuaTable result)
        {
            throw new DownloaderContractException("process_query must return a table.");
        }

        if (result.Keys.Count > MaxApiParameters)
        {
            throw new DownloaderContractException($"process_query returned more than {MaxApiParameters} parameters.");
        }

        var pairs = result.Keys.Cast<object>().Select(k =>
        {
            var key = ValidateNonEmptyString(k.ToString(), "process_query parameter name");
            var value = ValidateString(result[k]?.ToString(), "process_query parameter value");
            return $"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}";
        });

        return string.Join("&", pairs);
    }

    public void Dispose()
    {
        foreach (var lua in _luaContexts)
        {
            lua.Dispose();
        }
    }

    private static List<string> ReadStringTable(object? value, string functionName, int maxItems)
    {
        if (value is not NLua.LuaTable table)
        {
            throw new DownloaderContractException($"{functionName} must return a table of strings.");
        }

        if (table.Values.Count > maxItems)
        {
            throw new DownloaderContractException($"{functionName} returned more than {maxItems} items.");
        }

        return table.Values
            .Cast<object?>()
            .Select((item, index) => ValidateNonEmptyString(item as string, $"{functionName} result #{index + 1}"))
            .ToList();
    }

    private static string ValidateNonEmptyString(string? value, string name)
    {
        var text = ValidateString(value, name);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new DownloaderContractException($"{name} must not be empty.");
        }

        return text;
    }

    private static string ValidateString(string? value, string name)
    {
        if (value is null)
        {
            throw new DownloaderContractException($"{name} must be a string.");
        }

        if (value.Length > MaxStringLength)
        {
            throw new DownloaderContractException($"{name} must not exceed {MaxStringLength} characters.");
        }

        return value;
    }

    private sealed record DownloaderLuaFunction(
        DownloaderLuaContext Context,
        NLua.LuaFunction Function,
        string Operation)
    {
        public object[] Call(params object[] args) => Context.Call(Function, Operation, args);
    }
}
