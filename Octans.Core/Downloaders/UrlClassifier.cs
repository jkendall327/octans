using NLua;

namespace Octans.Core.Downloaders;

public class UrlClassifier
{
    public async Task<bool> Matches(string url)
    {
        var lua = new Lua();

        var raw = """
                  function match_url(url)
                      return string.find(url, "twitter%.com") ~= nil
                  end
                  """;
        
        lua.DoString(raw);
        
        var scriptFunc = lua ["match_url"] as LuaFunction;
        var res = scriptFunc?.Call(url)?.First();

        return (bool) res;
    }
}