using System.Collections.Concurrent;

namespace Octans.Core.Progress;

public class BackgroundProgressService : IBackgroundProgressReporter
{
    private readonly ConcurrentDictionary<Guid, ProgressStatus> _operations = new();

    public event EventHandler<ProgressEventArgs>? ProgressChanged;
    public event EventHandler<ProgressMessageEventArgs>? MessageReported;

    public ProgressHandle Start(string operation, int totalItems)
    {
        var status = new ProgressStatus
        {
            Id = Guid.NewGuid(),
            Operation = operation,
            TotalItems = totalItems,
            Processed = 0
        };

        _operations[status.Id] = status;
        Raise(status);
        return new ProgressHandle(status.Id, operation, totalItems);
    }

    public void Report(Guid id, int processed)
    {
        if (_operations.TryGetValue(id, out var status))
        {
            status.Processed = processed;
            Raise(status);
        }
    }

    public void Complete(Guid id)
    {
        if (_operations.TryRemove(id, out var status))
        {
            status.Processed = status.TotalItems;
            Raise(status, true);
        }
    }

    public void ReportMessage(string message)
    {
        MessageReported?.Invoke(this, new ProgressMessageEventArgs
        {
            Message = message,
            IsError = false
        });
    }

    public void ReportError(string message)
    {
        MessageReported?.Invoke(this, new ProgressMessageEventArgs
        {
            Message = message,
            IsError = true
        });
    }

    private void Raise(ProgressStatus status, bool completed = false)
    {
        ProgressChanged?.Invoke(this, new ProgressEventArgs
        {
            Id = status.Id,
            Operation = status.Operation,
            Processed = status.Processed,
            TotalItems = status.TotalItems,
            Completed = completed
        });
    }

    private class ProgressStatus
    {
        public Guid Id { get; init; }
        public string Operation { get; init; } = string.Empty;
        public int TotalItems { get; init; }
        public int Processed { get; set; }
    }
}
