using Microsoft.JSInterop;

namespace Octans.Client.Services;

public interface IThemeJsInterop
{
    Task<string> LoadThemePreferenceAsync();
    Task SetThemeAsync(string theme);
    Task SaveThemePreferenceAsync(string theme);
}

public class ThemeJsInterop(IJSRuntime jsRuntime) : IThemeJsInterop
{
    public async Task<string> LoadThemePreferenceAsync()
    {
        return await jsRuntime.InvokeAsync<string>("themeManager.loadThemePreference");
    }

    public async Task SetThemeAsync(string theme)
    {
        await jsRuntime.InvokeVoidAsync("themeManager.setTheme", theme);
    }

    public async Task SaveThemePreferenceAsync(string theme)
    {
        await jsRuntime.InvokeVoidAsync("themeManager.saveThemePreference", theme);
    }
}
