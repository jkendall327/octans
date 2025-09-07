using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Octans.Core.Progress;

public class BackgroundProgressService : IBackgroundProgressReporter
{
    private readonly ConcurrentDictionary<Guid, ProgressStatus> _operations = new();
    private readonly ILogger<BackgroundProgressService> _logger;

    public event EventHandler<ProgressEventArgs>? ProgressChanged;
    public event EventHandler<ProgressMessageEventArgs>? MessageReported;

    public BackgroundProgressService(ILogger<BackgroundProgressService> logger)
    {
        _logger = logger;
    }

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
        _logger.LogDebug("Started background operation {Operation} with {TotalItems} items ({Id})", operation, totalItems, status.Id);
        Raise(status);
        return new ProgressHandle(status.Id, operation, totalItems);
    }

    public void Report(Guid id, int processed)
    {
        if (_operations.TryGetValue(id, out var status))
        {
            status.Processed = processed;
            _logger.LogDebug("Progress for operation {Operation} ({Id}): {Processed}/{Total}", status.Operation, id, processed, status.TotalItems);
            Raise(status);
        }
    }

    public void Complete(Guid id)
    {
        if (_operations.TryRemove(id, out var status))
        {
            status.Processed = status.TotalItems;
            _logger.LogDebug("Completed operation {Operation} ({Id})", status.Operation, id);
            Raise(status, true);
        }
    }

    public void ReportMessage(string message)
    {
        _logger.LogDebug("Background message: {Message}", message);
        MessageReported?.Invoke(this, new ProgressMessageEventArgs
        {
            Message = message,
            IsError = false
        });
    }

    public void ReportError(string message)
    {
        _logger.LogDebug("Background error: {Message}", message);
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
