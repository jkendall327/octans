using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
namespace Octans.Core.Downloaders;

public class DownloaderFactory(
    IFileSystem fileSystem,
    IOptions<GlobalSettings> globalSettings,
    ILogger<DownloaderFactory> logger)
{
    private readonly GlobalSettings _globalSettings = globalSettings.Value;

    private readonly List<Downloader> _downloaders = [];

    public string DownloaderDirectory => fileSystem.Path.Join(_globalSettings.AppRoot, "downloaders");

    public virtual async Task<List<Downloader>> GetDownloaders()
    {
        if (_downloaders.Any())
        {
            return _downloaders;
        }

        var downloaders = fileSystem.DirectoryInfo.New(DownloaderDirectory);

        if (!downloaders.Exists)
        {
            logger.LogError("Downloader folder doesn't exist");
            throw new InvalidOperationException("Downloader folder doesn't exist");
        }

        foreach (var subdir in downloaders.EnumerateDirectories())
        {
            logger.LogInformation("Creating downloader from {DownloaderDirectory}", subdir.Name);

            var files = subdir.EnumerateFiles("*.lua", SearchOption.TopDirectoryOnly).ToList();

            var downloader = await Create(files);

            if (downloader is null)
            {
                // TODO: Handle specific creation exceptions or surface errors to UI.
                // exception on creation
                continue;
            }

            _downloaders.Add(downloader);
        }

        logger.LogInformation("Created {DownloaderCount} downloaders", _downloaders.Count);

        return _downloaders;
    }

    public async Task<List<Downloader>> Rescan()
    {
        foreach (var downloader in _downloaders)
        {
            downloader.Dispose();
        }

        _downloaders.Clear();

        return await GetDownloaders();
    }

    private async Task<Downloader?> Create(List<IFileInfo> sources)
    {
        string[] names = ["metadata", "classifier", "parser", "gug", "api"];

        var functions = new Dictionary<string, DownloaderLuaContext>();

        var metadata = new DownloaderMetadata();

        foreach (var name in names)
        {
            var file = sources.SingleOrDefault(s =>
            {
                var clean = fileSystem.Path.GetFileNameWithoutExtension(s.Name).ToLowerInvariant();
                return string.Equals(clean, name, StringComparison.OrdinalIgnoreCase);
            });

            if (file is null) continue;

            logger.LogInformation("Read file content for {LuaFile}", file.Name);

            var raw = await fileSystem.File.ReadAllTextAsync(file.FullName);

            if (name is "metadata")
            {
                metadata = ExtractMetadata(raw);

                if (metadata is null) return null;

                continue;
            }

            var lua = DownloaderLuaContext.Create();

            try
            {
                lua.DoString(raw, $"{name}.lua");
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error loading raw Lua string");
                lua.Dispose();
                foreach (var loadedLua in functions.Values)
                {
                    loadedLua.Dispose();
                }

                return null;
            }

            logger.LogInformation("Instantiated Lua from {LuaFile}", file.Name);
            functions.Add(name, lua);
        }

        try
        {
            return new(functions, metadata);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error creating downloader from Lua functions");
            foreach (var lua in functions.Values)
            {
                lua.Dispose();
            }

            return null;
        }
    }

    private DownloaderMetadata? ExtractMetadata(string raw)
    {
        using var lua = DownloaderLuaContext.Create();

        try
        {
            lua.DoString(raw, "metadata.lua");
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error extracting metadata");
            return null;
        }

        var downloaderTable = lua.GetTable("Downloader");

        if (downloaderTable == null)
        {
            logger.LogError("Downloader metadata table not found");
            return null;
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
