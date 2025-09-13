using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Octans.Core;

namespace Octans.Client.Components.Settings;

public sealed class SettingsViewModel(
    IOptions<GlobalSettings> globalSettings,
    ILogger<SettingsViewModel> logger,
    IConfiguration configuration,
    IJSRuntime jsRuntime,
    ThemeService themeService) : IAsyncDisposable
{
    public SettingsContext Context { get; } = new();
    public SettingsModel Settings { get; } = new();
    public List<string> AvailableLogLevels { get; } =
        ["Trace", "Debug", "Information", "Warning", "Error", "Critical", "None"];

    public SettingsPageDescriptor? ActivePage { get; set; }
    public string SearchText { get; set; } = string.Empty;

    public bool IsSaving { get; private set; }
    public bool SaveSuccess { get; private set; }
    public bool SaveError { get; private set; }
    public string ErrorMessage { get; private set; } = string.Empty;

    public Func<Task>? NotifyStateChanged { get; set; }

    public async Task InitializeAsync()
    {
        themeService.OnThemeChanged += ApplyTheme;
        var savedTheme = await jsRuntime.InvokeAsync<string>("themeManager.loadThemePreference");
        if (!string.IsNullOrEmpty(savedTheme))
        {
            Settings.Theme = savedTheme;
            await themeService.SetTheme(savedTheme);
        }
        else
        {
            Settings.Theme = themeService.CurrentTheme;
        }

        await ApplyTheme();
        LoadConfiguration();
    }

    private void LoadConfiguration()
    {
        Settings.AppRoot = globalSettings.Value.AppRoot;
        Settings.LogLevel = configuration.GetValue<string>("Logging:LogLevel:Default") ?? "Information";
        Settings.AspNetCoreLogLevel = configuration.GetValue<string>("Logging:LogLevel:Microsoft.AspNetCore") ?? "Warning";
        Settings.ImportSource = configuration.GetValue<string>("ImportSource") ?? string.Empty;
        Settings.TagColor = configuration.GetValue<string>("TagColor") ?? "#000000";
    }

    public async Task ThemeChanged()
    {
        await themeService.SetTheme(Settings.Theme);
    }

    private async Task ApplyTheme()
    {
        await jsRuntime.InvokeVoidAsync("themeManager.setTheme", themeService.CurrentTheme);
        await jsRuntime.InvokeVoidAsync("themeManager.saveThemePreference", themeService.CurrentTheme);
    }

    public async Task SaveConfiguration()
    {
        IsSaving = true;
        SaveSuccess = false;
        SaveError = false;

        await OnStateChanged();
        
        try
        {
            logger.LogInformation("Saving configuration settings");
            
            await Task.Delay(500);
            
            SaveSuccess = true;

            await OnStateChanged();
            
            await Task.Delay(3000);
            
            SaveSuccess = false;
        }
        catch (Exception ex)
        {
            SaveError = true;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task OnStateChanged()
    {
        if (NotifyStateChanged is not null)
        {
            await NotifyStateChanged.Invoke();
        }
    }

    public ValueTask DisposeAsync()
    {
        themeService.OnThemeChanged -= ApplyTheme;
        return ValueTask.CompletedTask;
    }
}