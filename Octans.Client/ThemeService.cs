namespace Octans.Client;

public class ThemeService
{
    public string CurrentTheme { get; private set; } = "light";

    public event Action? OnThemeChanged;

    public void SetTheme(string theme)
    {
        if (theme == CurrentTheme)
        {
            return;
        }

        CurrentTheme = theme;
        OnThemeChanged?.Invoke();
    }
}