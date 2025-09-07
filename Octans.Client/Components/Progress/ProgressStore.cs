using System.Collections.Concurrent;
using Octans.Core.Progress;

namespace Octans.Client.Components.Progress;

public sealed class ProgressStore : IDisposable
{
    private readonly IBackgroundProgressReporter _reporter;
    private readonly ConcurrentDictionary<Guid, ProgressEntry> _entries = new();
    private readonly ConcurrentDictionary<Guid, MessageEntry> _messages = new();

    public event Func<Task>? OnChange;

    public ICollection<ProgressEntry> Entries => _entries.Values;
    public ICollection<MessageEntry> Messages => _messages.Values;

    public ProgressStore(IBackgroundProgressReporter reporter)
    {
        _reporter = reporter;
        _reporter.ProgressChanged += HandleProgressChanged;
        _reporter.MessageReported += HandleMessageReported;
    }

    // TODO: come up with a proper way to avoid 'async void' here.
    // Do some proper message passing with Mediator or whatever?
    private async void HandleProgressChanged(object? sender, ProgressEventArgs e)
    {
        if (e.Completed)
        {
            _entries.TryRemove(e.Id, out _);
        }
        else
        {
            _entries[e.Id] = new(e.Id, e.Operation, e.Processed, e.TotalItems);
        }

        var handler = OnChange;

        if (handler != null)
        {
            await handler();
        }
    }

    private async void HandleMessageReported(object? sender, ProgressMessageEventArgs e)
    {
        var id = Guid.NewGuid();
        _messages[id] = new(id, e.Message, e.IsError);
        var handler = OnChange;

        if (handler != null)
        {
            await handler();
        }
    }

    public async Task RemoveMessage(Guid id)
    {
        _messages.TryRemove(id, out _);
        var handler = OnChange;

        if (handler != null)
        {
            await handler();
        }
    }

    public void Dispose()
    {
        _reporter.ProgressChanged -= HandleProgressChanged;
        _reporter.MessageReported -= HandleMessageReported;
    }
}

public record ProgressEntry(Guid Id, string Operation, int Processed, int TotalItems);

public record MessageEntry(Guid Id, string Message, bool IsError);