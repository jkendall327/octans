namespace Octans.Core.Progress;

public class ProgressEventArgs : EventArgs
{
    public Guid Id { get; init; }
    public string Operation { get; init; } = string.Empty;
    public int Processed { get; init; }
    public int TotalItems { get; init; }
    public bool Completed { get; init; }
}
