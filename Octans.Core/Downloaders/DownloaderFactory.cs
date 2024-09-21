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
        var path = _fileSystem.Path.Join(_globalSettings.AppRoot, "downloaders");
        var downloaders = _fileSystem.DirectoryInfo.New(path);

        if (!downloaders.Exists)
        {
            throw new InvalidOperationException();
        }
        
        var d = new List<Downloader>();
        
        foreach (var subdir in downloaders.EnumerateDirectories())
        {
            var files = subdir.EnumerateFiles("*.lua", SearchOption.TopDirectoryOnly).ToList();

            var downloader = await Create(files);

            if (downloader is null)
            {
                // exception on creation
                continue;
            }
            
            d.Add(downloader);
        }

        return d;
    }

    private async Task<Downloader?> Create(List<IFileInfo> sources)
    {
        string[] names = ["metadata", "classifier", "parser", "gug", "api"];

        var functions = new Dictionary<string, Lua>();
        
        foreach (var name in names)
        {
            var file = sources.SingleOrDefault(s =>
            {
                var clean = _fileSystem.Path.GetFileNameWithoutExtension(s.Name).ToLower();
                return string.Equals(clean, name, StringComparison.InvariantCultureIgnoreCase);
            });

            if (file is null) continue;
            
            var raw = await _fileSystem.File.ReadAllTextAsync(file.FullName);
            
            var lua = new Lua();

            try
            {
                lua.DoString(raw);
            }
            catch (Exception e)
            {
                return null;
            }
            
            functions.Add(name, lua);
        }

        return new(functions);
    }
}