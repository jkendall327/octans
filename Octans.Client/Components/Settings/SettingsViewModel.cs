using Octans.Client.Services;
using Octans.Client.Settings;
using Octans.Core.Communication;

namespace Octans.Client.Components.Settings;

public sealed class SettingsViewModel(
    ISettingsService settingsService,
    ILogger<SettingsViewModel> logger,
    IThemePreferenceService themeJsInterop,
    ThemeService themeService,
    TimeProvider timeProvider) : IDisposable, INotifyStateChanged
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

    public Func<Task>? StateChanged { get; set; }

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

        var savedTheme = await themeJsInterop.LoadThemePreference();
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
        await themeJsInterop.SetTheme(themeService.CurrentTheme);
        await themeJsInterop.SaveThemePreference(themeService.CurrentTheme);
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

            await Task.Delay(TimeSpan.FromSeconds(3), timeProvider);

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
        if (StateChanged is not null)
        {
            await StateChanged.Invoke();
        }
    }

    public void Dispose()
    {
        themeService.OnThemeChanged -= ApplyTheme;
    }
}