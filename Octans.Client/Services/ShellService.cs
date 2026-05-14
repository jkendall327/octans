namespace Octans.Client;

public class ShellService
{
    public bool IsShellVisible { get; private set; } = true;

    public event Action? ShellVisibilityChanged;

    public void ShowShell()
    {
        if (!IsShellVisible)
        {
            IsShellVisible = true;
            ShellVisibilityChanged?.Invoke();
        }
    }

    public void HideShell()
    {
        if (IsShellVisible)
        {
            IsShellVisible = false;
            ShellVisibilityChanged?.Invoke();
        }
    }
}