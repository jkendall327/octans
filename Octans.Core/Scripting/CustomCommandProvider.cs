using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NLua;

namespace Octans.Core.Scripting;

public record ImageCommandMetadata(string Name, string Description, string Icon);

public sealed class CustomCommandProvider(
    IFileSystem fileSystem,
    IOptions<GlobalSettings> globalSettings,
    ILogger<CustomCommandProvider> logger) : ICustomCommandProvider
{
    public async Task<List<CustomCommand>> GetCustomCommandsAsync()
    {
        var globalSettingsValue = globalSettings.Value;
        var commandsPath = fileSystem.Path.Join(globalSettingsValue.AppRoot, "commands", "image-commands");
        var commandsDirectory = fileSystem.DirectoryInfo.New(commandsPath);

        if (!commandsDirectory.Exists)
        {
            logger.LogDebug("Image commands directory doesn't exist at {CommandsPath}", commandsPath);
            return [];
        }

        var luaFiles = commandsDirectory.EnumerateFiles("*.lua", SearchOption.AllDirectories).ToList();
        var commands = new List<CustomCommand>();

        foreach (var luaFile in luaFiles)
        {
            try
            {
                var command = await CreateCustomCommand(luaFile);
                if (command is not null)
                {
                    commands.Add(command);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create custom command from {FilePath}", luaFile.FullName);
            }
        }

        logger.LogInformation("Loaded {CommandCount} custom image commands", commands.Count);
        return commands;
    }

    private async Task<CustomCommand?> CreateCustomCommand(IFileInfo luaFile)
    {
        var luaContent = await fileSystem.File.ReadAllTextAsync(luaFile.FullName);

        var metadata = ExtractMetadata(luaContent);
        if (metadata is null)
        {
            logger.LogWarning("Failed to extract metadata from {FilePath}", luaFile.FullName);
            return null;
        }

        var executeAction = CreateExecuteAction(luaFile.FullName, luaContent);

        return new CustomCommand(metadata.Name, metadata.Description, metadata.Icon, executeAction);
    }

    private ImageCommandMetadata? ExtractMetadata(string luaContent)
    {
        using var lua = new Lua();

        try
        {
            lua.DoString(luaContent);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error parsing Lua content for metadata extraction");
            return null;
        }

        var commandTable = lua.GetTable("ImageCommand");
        if (commandTable == null)
        {
            logger.LogWarning("ImageCommand table not found in Lua script");
            return null;
        }

        var name = commandTable["name"]?.ToString() ?? "Unknown Command";
        var description = commandTable["description"]?.ToString() ?? "No description provided";
        var icon = commandTable["icon"]?.ToString() ?? "fa-solid fa-code";

        return new ImageCommandMetadata(name, description, icon);
    }

    private Func<string, Task> CreateExecuteAction(string scriptPath, string luaContent)
    {
        return imageUrl =>
        {
            try
            {
                ExecuteLuaScript(scriptPath, luaContent, imageUrl);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to execute Lua script {ScriptPath} for image {ImageUrl}", scriptPath, imageUrl);
            }
            return Task.CompletedTask;
        };
    }

    private void ExecuteLuaScript(string scriptPath, string luaContent, string imageUrl)
    {
        using var lua = new Lua();

        try
        {
            lua.DoString(luaContent);

            var executeFunction = lua["execute"] as LuaFunction;
            if (executeFunction is null)
            {
                logger.LogWarning("No 'execute' function found in Lua script {ScriptPath}", scriptPath);
                return;
            }

            logger.LogDebug("Executing Lua script {ScriptPath} with image URL {ImageUrl}", scriptPath, imageUrl);
            executeFunction.Call(imageUrl);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing Lua script {ScriptPath}", scriptPath);
            throw;
        }
    }
}