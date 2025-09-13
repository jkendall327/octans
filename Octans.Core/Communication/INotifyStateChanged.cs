namespace Octans.Core.Communication;

public interface INotifyStateChanged
{
    Func<Task>? StateChanged { get; }
}