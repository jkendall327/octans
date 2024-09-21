using NLua;

namespace Octans.Core.Downloaders;

public class Downloader
{
    private readonly Dictionary<string, Lua> _functions;

    private readonly LuaFunction _matchUrl;
    
    public Downloader(Dictionary<string, Lua> functions)
    {
        _functions = functions;
        
        _matchUrl = _functions["classifier"]["match_url"] as LuaFunction ?? throw new InvalidOperationException();
    }

    public bool MatchesUrl(string url)
    {
        var res = _matchUrl.Call(url)?.FirstOrDefault();

        if (res is bool b)
        {
            return b;
        }

        throw new InvalidOperationException("Bad script");
    }
}