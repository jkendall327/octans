using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Octans.Client.Settings;

public interface ISettingsService
{
    Task<SettingsModel> LoadAsync();
    Task SaveAsync(SettingsModel model);
}

public class SettingsService(
    IConfiguration configuration,
    IFileSystem fileSystem,
    IHostEnvironment environment,
    ILogger<SettingsService> logger) : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _settingsPath = fileSystem.Path.Combine(environment.ContentRootPath, "usersettings.json");

    public Task<SettingsModel> LoadAsync()
    {
        var model = new SettingsModel
        {
            Theme = configuration.GetValue<string>("UserSettings:Theme") ?? "light",
            AppRoot = configuration.GetValue<string>("GlobalSettings:AppRoot") ?? string.Empty,
            LogLevel = configuration.GetValue<string>("Logging:LogLevel:Default") ?? "Information",
            AspNetCoreLogLevel = configuration.GetValue<string>("Logging:LogLevel:Microsoft.AspNetCore") ?? "Warning",
            ImportSource = configuration.GetValue<string>("UserSettings:ImportSource") ?? string.Empty,
            TagColor = configuration.GetValue<string>("UserSettings:TagColor") ?? "#000000"
        };

        return Task.FromResult(model);
    }

    public async Task SaveAsync(SettingsModel model)
    {
        var contents = new
        {
            UserSettings = new
            {
                model.Theme,
                model.ImportSource,
                model.TagColor
            },
            GlobalSettings = new
            {
                model.AppRoot
            },
            Logging = new
            {
                LogLevel = new Dictionary<string, string>
                {
                    ["Default"] = model.LogLevel,
                    ["Microsoft.AspNetCore"] = model.AspNetCoreLogLevel
                }
            }
        };

        try
        {
            var options = SerializerOptions;
            var json = JsonSerializer.Serialize(contents, options);
            await fileSystem.File.WriteAllTextAsync(_settingsPath, json);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write settings file: {Path}", _settingsPath);

            throw;
        }
    }
}