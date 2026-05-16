namespace Octans.Client.Components;

public abstract class ViewmodelBase
{
    public event Func<Task>? StateChanged;

    protected async Task NotifyStateChanged()
    {
        var handler = StateChanged;
        if (handler is not null)
        {
            await handler.Invoke();
        }
    }
}
