using Microsoft.JSInterop;

namespace Octans.Client.Services;

public interface IThemePreferenceService
{
    Task<string> LoadThemePreference(CancellationToken ct = default);
    Task SetTheme(string theme, CancellationToken ct = default);
    Task SaveThemePreference(string theme, CancellationToken ct = default);
}

public class ThemePreferenceService(IJSRuntime jsRuntime) : IThemePreferenceService
{
    public async Task<string> LoadThemePreference(CancellationToken ct = default)
    {
        return await jsRuntime.InvokeAsync<string>("themeManager.loadThemePreference", ct);
    }

    public async Task SetTheme(string theme, CancellationToken ct = default)
    {
        await jsRuntime.InvokeVoidAsync("themeManager.setTheme", ct, theme);
    }

    public async Task SaveThemePreference(string theme, CancellationToken ct = default)
    {
        await jsRuntime.InvokeVoidAsync("themeManager.saveThemePreference", ct, theme);
    }
}
