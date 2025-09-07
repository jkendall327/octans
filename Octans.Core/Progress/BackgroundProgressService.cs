using System.Collections.Concurrent;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Octans.Core.Progress;

public class BackgroundProgressService : IBackgroundProgressReporter
{
    private readonly ConcurrentDictionary<Guid, ProgressStatus> _operations = new();
    private readonly ILogger<BackgroundProgressService> _logger;
    private readonly IMediator _mediator;

    public BackgroundProgressService(ILogger<BackgroundProgressService> logger, IMediator mediator)
    {
        _logger = logger;
        _mediator = mediator;
    }

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

        _logger.LogDebug("Started background operation {Operation} with {TotalItems} items ({Id})",
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

            _logger.LogDebug("Progress for operation {Operation} ({Id}): {Processed}/{Total}",
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
            _logger.LogDebug("Completed operation {Operation} ({Id})", status.Operation, id);
            await Raise(status, true);
        }
    }

    public async Task ReportMessage(string message)
    {
        _logger.LogDebug("Background message: {Message}", message);
        await _mediator.Publish(new ProgressMessage(message, false));
    }

    public async Task ReportError(string message)
    {
        _logger.LogDebug("Background error: {Message}", message);
        await _mediator.Publish(new ProgressMessage(message, true));
    }

    private async Task Raise(ProgressStatus status, bool completed = false)
    {
        await _mediator.Publish(new ProgressStatus
        {
            Id = status.Id,
            Operation = status.Operation,
            Processed = status.Processed,
            TotalItems = status.TotalItems,
            Completed = completed
        });
    }
}