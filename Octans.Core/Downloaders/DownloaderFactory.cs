using System.IO.Abstractions;
using NLua;

namespace Octans.Core.Downloaders;

public class DownloaderFactory
{
    private readonly IFileSystem _fileSystem;
    private readonly GlobalSettings _globalSettings;

    public DownloaderFactory(IFileSystem fileSystem, GlobalSettings globalSettings)
    {
        _fileSystem = fileSystem;
        _globalSettings = globalSettings;
    }

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