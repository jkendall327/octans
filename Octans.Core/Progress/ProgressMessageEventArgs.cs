namespace Octans.Core.Progress;

public class ProgressMessageEventArgs : EventArgs
{
    public required string Message { get; init; }
    public bool IsError { get; init; }
}
