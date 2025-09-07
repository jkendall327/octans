using Mediator;

namespace Octans.Core.Progress;

public class ProgressStatus : INotification
{
    public Guid Id { get; init; }
    public string Operation { get; init; } = string.Empty;
    public int TotalItems { get; init; }
    public int Processed { get; set; }
}

public class ProgressMessage(string Message, bool IsError) : INotification;