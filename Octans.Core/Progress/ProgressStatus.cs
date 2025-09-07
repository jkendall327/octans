using System.Collections.Concurrent;
using Mediator;

namespace Octans.Core.Progress;

public class ProgressStatus : INotification
{
    public Guid Id { get; init; }
    public string Operation { get; init; } = string.Empty;
    public int TotalItems { get; init; }
    public int Processed { get; set; }
    public bool Completed { get; init; }
}

public record ProgressMessage(string Message, bool IsError) : INotification;

public sealed class ProgressStore : INotificationHandler<ProgressStatus>, INotificationHandler<ProgressMessage>
{
    private readonly ConcurrentDictionary<Guid, ProgressEntry> _entries = new();
    private readonly ConcurrentDictionary<Guid, MessageEntry> _messages = new();

    public event Func<Task>? OnChange;

    public ICollection<ProgressEntry> Entries => _entries.Values;
    public ICollection<MessageEntry> Messages => _messages.Values;

    public async Task RemoveMessage(Guid id)
    {
        _messages.TryRemove(id, out _);
        var handler = OnChange;

        if (handler != null)
        {
            await handler();
        }
    }

    public async ValueTask Handle(ProgressStatus e, CancellationToken cancellationToken)
    {
        if (e.Completed)
        {
            _entries.TryRemove(e.Id, out _);
        }
        else
        {
            _entries[e.Id] = new(e.Id, e.Operation, e.Processed, e.TotalItems);
        }

        if (OnChange != null)
        {
            await OnChange();
        }
    }

    public async ValueTask Handle(ProgressMessage e, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        
        _messages[id] = new(id, e.Message, e.IsError);
        
        if (OnChange != null)
        {
            await OnChange();
        }
    }
}

public record ProgressEntry(Guid Id, string Operation, int Processed, int TotalItems);

public record MessageEntry(Guid Id, string Message, bool IsError);