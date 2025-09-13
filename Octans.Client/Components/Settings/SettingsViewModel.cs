using Microsoft.JSInterop;
using Octans.Client.Settings;

namespace Octans.Client.Components.Settings;

public sealed class SettingsViewModel(
    ISettingsService settingsService,
    ILogger<SettingsViewModel> logger,
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

        var loaded = await settingsService.LoadAsync();

        Settings.Theme = loaded.Theme;
        Settings.AppRoot = loaded.AppRoot;
        Settings.LogLevel = loaded.LogLevel;
        Settings.AspNetCoreLogLevel = loaded.AspNetCoreLogLevel;
        Settings.ImportSource = loaded.ImportSource;
        Settings.TagColor = loaded.TagColor;

        var savedTheme = await jsRuntime.InvokeAsync<string>("themeManager.loadThemePreference");
        if (!string.IsNullOrEmpty(savedTheme))
        {
            Settings.Theme = savedTheme;
        }

        await themeService.SetTheme(Settings.Theme);
        await ApplyTheme();
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
            
            await settingsService.SaveAsync(Settings);

            SaveSuccess = true;

            await OnStateChanged();

            await Task.Delay(3000);

            SaveSuccess = false;
        }
        catch (Exception ex)
        {
            SaveError = true;
            ErrorMessage = ex.Message;
            logger.LogError(ex, "Error saving configuration");
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