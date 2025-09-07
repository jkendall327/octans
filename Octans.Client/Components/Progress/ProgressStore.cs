using Octans.Core.Progress;

namespace Octans.Client.Components.Progress;

public class ProgressStore : IDisposable
{
    private readonly IBackgroundProgressReporter _reporter;
    private readonly Dictionary<Guid, ProgressEntry> _entries = new();
    private readonly List<MessageEntry> _messages = new();

    public event Action? OnChange;

    public IReadOnlyCollection<ProgressEntry> Entries => _entries.Values;
    public IReadOnlyCollection<MessageEntry> Messages => _messages;

    public ProgressStore(IBackgroundProgressReporter reporter)
    {
        _reporter = reporter;
        _reporter.ProgressChanged += HandleProgressChanged;
        _reporter.MessageReported += HandleMessageReported;
    }

    private void HandleProgressChanged(object? sender, ProgressEventArgs e)
    {
        if (e.Completed)
        {
            _entries.Remove(e.Id);
        }
        else
        {
            _entries[e.Id] = new ProgressEntry(e.Id, e.Operation, e.Processed, e.TotalItems);
        }

        OnChange?.Invoke();
    }

    private void HandleMessageReported(object? sender, ProgressMessageEventArgs e)
    {
        _messages.Add(new MessageEntry(Guid.NewGuid(), e.Message, e.IsError));
        OnChange?.Invoke();
    }

    public void RemoveMessage(Guid id)
    {
        _messages.RemoveAll(m => m.Id == id);
        OnChange?.Invoke();
    }

    public void Dispose()
    {
        _reporter.ProgressChanged -= HandleProgressChanged;
        _reporter.MessageReported -= HandleMessageReported;
    }
}

public record ProgressEntry(Guid Id, string Operation, int Processed, int TotalItems);
public record MessageEntry(Guid Id, string Message, bool IsError);
