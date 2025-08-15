namespace Octans.Client;

public class ThemeService
{
    public string CurrentTheme { get; private set; } = "light";

    public event Func<Task>? OnThemeChanged;

    public async Task SetTheme(string theme)
    {
        if (theme == CurrentTheme)
        {
            return;
        }

        CurrentTheme = theme;

        if (OnThemeChanged != null)
        {
            await OnThemeChanged.Invoke();
        }
    }
}