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
        
        var metadata = new DownloaderMetadata();
        
        foreach (var name in names)
        {
            var file = sources.SingleOrDefault(s =>
            {
                var clean = _fileSystem.Path.GetFileNameWithoutExtension(s.Name).ToLower();
                return string.Equals(clean, name, StringComparison.InvariantCultureIgnoreCase);
            });

            if (file is null) continue;
            
            var raw = await _fileSystem.File.ReadAllTextAsync(file.FullName);

            if (name is "metadata")
            {
                metadata = ExtractMetadata(raw);
                
                if (metadata is null) return null;
                
                continue;
            }
            
            var lua = new Lua();
            
            try
            {
                lua.DoString(raw);

                functions.Add(name, lua);
            }
            catch (Exception e)
            {
                return null;
            }
            
            functions.Add(name, lua);
        }

        return new(functions, metadata);
    }
    
    private DownloaderMetadata? ExtractMetadata(string raw)
    {
        Lua lua = new();

        try
        {
            lua.DoString(raw);
        }
        catch (Exception e)
        {
            return null;
        }
        
        var downloaderTable = lua.GetTable("Downloader");

        if (downloaderTable == null)
        {
            throw new InvalidOperationException("Downloader metadata table not found");
        }

        var metadata = new DownloaderMetadata
        {
            Name = downloaderTable["name"]?.ToString() ?? string.Empty,
            Creator = downloaderTable["creator"]?.ToString() ?? string.Empty,
            Homepage = downloaderTable["homepage"]?.ToString() ?? string.Empty
        };

        if (Version.TryParse(downloaderTable["version"]?.ToString(), out var version))
        {
            metadata.Version = version;
        }

        return metadata;
    }
}