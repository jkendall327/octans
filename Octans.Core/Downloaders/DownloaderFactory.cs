using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NLua;

namespace Octans.Core.Downloaders;

public class DownloaderFactory
{
    private readonly IFileSystem _fileSystem;
    private readonly GlobalSettings _globalSettings;
    private readonly ILogger<DownloaderFactory> _logger;

    public DownloaderFactory(IFileSystem fileSystem, IOptions<GlobalSettings> globalSettings, ILogger<DownloaderFactory> logger)
    {
        _fileSystem = fileSystem;
        _globalSettings = globalSettings.Value;
        _logger = logger;
    }

    private readonly List<Downloader> _downloaders = new();

    public async Task<List<Downloader>> GetDownloaders()
    {
        if (_downloaders.Any())
        {
            return _downloaders;
        }
        
        var path = _fileSystem.Path.Join(_globalSettings.AppRoot, "downloaders");
        var downloaders = _fileSystem.DirectoryInfo.New(path);

        if (!downloaders.Exists)
        {
            _logger.LogError("Downloader folder doesn't exist");
            throw new InvalidOperationException();
        }
        
        foreach (var subdir in downloaders.EnumerateDirectories())
        {
            _logger.LogInformation("Creating downloader from {DownloaderDirectory}", subdir.Name);
            
            var files = subdir.EnumerateFiles("*.lua", SearchOption.TopDirectoryOnly).ToList();

            var downloader = await Create(files);

            if (downloader is null)
            {
                // exception on creation
                continue;
            }
            
            _downloaders.Add(downloader);
        }

        _logger.LogInformation("Created {DownloaderCount} downloaders", _downloaders.Count);
        
        return _downloaders;
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
            
            _logger.LogInformation("Read file content for {LuaFile}", file.Name);
            
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
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error loading raw Lua string");
                return null;
            }
            
            _logger.LogInformation("Instantiated Lua from {LuaFile}", file.Name);
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
            _logger.LogError(e, "Error extracting metadata");
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