using Microsoft.Extensions.Options;
using Octans.Core;
using Octans.Core.Communication;

namespace Octans.Client.Config;

public class ConfigViewModel
{
    private readonly IOptions<GlobalSettings> _globalSettings;
    private readonly ILogger<ConfigViewModel> _logger;
    private readonly IConfiguration _configuration;

    public ConfigViewModel(
        IOptions<GlobalSettings> globalSettings,
        ILogger<ConfigViewModel> logger,
        IConfiguration configuration)
    {
        _globalSettings = globalSettings;
        _logger = logger;
        _configuration = configuration;
        
        // Initialize properties from configuration
        LoadConfiguration();
    }

    public string ApiUrl { get; set; } = string.Empty;
    public string AppRoot { get; set; } = string.Empty;
    public string LogLevel { get; set; } = "Information";
    public string AspNetCoreLogLevel { get; set; } = "Warning";
    
    public List<string> AvailableLogLevels { get; } = ["Trace", "Debug", "Information", "Warning", "Error", "Critical", "None"];

    public async Task SaveConfiguration()
    {
        try
        {
            _logger.LogInformation("Saving configuration settings");
            
            // This would actually update the appsettings.json file or use another method
            // to persist the settings. For now, we're just simulating this operation.
            
            await Task.Delay(500); // Simulate saving
            
            _logger.LogInformation("Configuration settings saved successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving configuration settings");
            throw;
        }
    }

    private void LoadConfiguration()
    {
        // Get API URL from configuration
        var port = CommunicationConstants.OCTANS_SERVER_PORT;
        ApiUrl = $"http://localhost:{port}/";
        
        // Get App Root from global settings
        AppRoot = _globalSettings.Value.AppRoot;
        
        // Get logging settings
        LogLevel = _configuration.GetValue<string>("Logging:LogLevel:Default") ?? "Information";
        AspNetCoreLogLevel = _configuration.GetValue<string>("Logging:LogLevel:Microsoft.AspNetCore") ?? "Warning";
    }
}