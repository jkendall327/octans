using System.IO.Abstractions;
using NLua;

namespace Octans.Core.Downloaders;

public class UrlClassifier
{
    private readonly DownloaderFactory _factory;

    public UrlClassifier(DownloaderFactory factory)
    {
        _factory = factory;
    }

    public async Task<Downloader?> Matches(string url)
    {
        var downloaders = await _factory.GetDownloaders();
        
        foreach (var downloader in downloaders)
        {
            var matches = downloader.MatchesUrl(url);

            if (matches)
            {
                return downloader;
            }
        }

        return null;
    }
}

public class DownloaderFactory
{
    private readonly IFileSystem _fileSystem;
    private readonly GlobalSettings _globalSettings;

    public async Task<List<Downloader>> GetDownloaders()
    {
        var downloaders = _fileSystem.Path.Join(_globalSettings.AppRoot, "downloaders");
        var dirs = _fileSystem.DirectoryInfo.New(downloaders);

        var d = new List<Downloader>();
        
        foreach (var subdir in dirs.EnumerateDirectories())
        {
            var files = subdir.EnumerateFiles("*.lua", SearchOption.TopDirectoryOnly).ToList();

            var downloader = await Create(files);

            d.Add(downloader);
        }

        return d;
    }

    private async Task<Downloader> Create(List<IFileInfo> sources)
    {
        string[] names = ["metadata", "classifier", "parser", "gug", "api"];

        var functions = new Dictionary<string, Lua>();
        
        foreach (var name in names)
        {
            var file = sources.SingleOrDefault(s => string.Equals(s.Name, name, StringComparison.InvariantCultureIgnoreCase));

            if (file is null) continue;
            
            var raw = await File.ReadAllTextAsync(file.FullName);
            
            var lua = new Lua();

            lua.DoString(raw);
            
            functions.Add(name, lua);
        }

        return new(functions);
    }
}

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
        var res = _matchUrl.Call(url)?.First();

        if (res is bool b)
        {
            return b;
        }

        throw new InvalidOperationException("Bad script");
    }
}