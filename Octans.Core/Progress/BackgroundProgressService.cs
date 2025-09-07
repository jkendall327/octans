using System.Collections.Concurrent;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Octans.Core.Progress;

public readonly record struct ProgressHandle(Guid Id, string Operation, int TotalItems);

public interface IBackgroundProgressReporter
{
    Task<ProgressHandle> Start(string operation, int totalItems);
    Task Report(Guid id, int processed);
    Task Complete(Guid id);
    Task ReportMessage(string message);
    Task ReportError(string message);
}

public class BackgroundProgressService(ILogger<BackgroundProgressService> logger, IMediator mediator)
    : IBackgroundProgressReporter
{
    private readonly ConcurrentDictionary<Guid, ProgressStatus> _operations = new();

    public async Task<ProgressHandle> Start(string operation, int totalItems)
    {
        var status = new ProgressStatus
        {
            Id = Guid.NewGuid(),
            Operation = operation,
            TotalItems = totalItems,
            Processed = 0
        };

        _operations[status.Id] = status;

        logger.LogDebug("Started background operation {Operation} with {TotalItems} items ({Id})",
            operation,
            totalItems,
            status.Id);

        await Raise(status);

        return new(status.Id, operation, totalItems);
    }

    public async Task Report(Guid id, int processed)
    {
        if (_operations.TryGetValue(id, out var status))
        {
            status.Processed = processed;

            logger.LogDebug("Progress for operation {Operation} ({Id}): {Processed}/{Total}",
                status.Operation,
                id,
                processed,
                status.TotalItems);

            await Raise(status);
        }
    }

    public async Task Complete(Guid id)
    {
        if (_operations.TryRemove(id, out var status))
        {
            status.Processed = status.TotalItems;
            logger.LogDebug("Completed operation {Operation} ({Id})", status.Operation, id);
            await Raise(status, true);
        }
    }

    public async Task ReportMessage(string message)
    {
        logger.LogDebug("Background message: {Message}", message);
        await mediator.Publish(new ProgressMessage(message, false));
    }

    public async Task ReportError(string message)
    {
        logger.LogDebug("Background error: {Message}", message);
        await mediator.Publish(new ProgressMessage(message, true));
    }

    private async Task Raise(ProgressStatus status, bool completed = false)
    {
        await mediator.Publish(new ProgressStatus
        {
            Id = status.Id,
            Operation = status.Operation,
            Processed = status.Processed,
            TotalItems = status.TotalItems,
            Completed = completed
        });
    }
}