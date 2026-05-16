using Octans.Core.Progress;

namespace Octans.Tests.Infrastructure;

public sealed class SpyProgressReporter : IBackgroundProgressReporter
{
    private readonly List<(string Operation, int TotalItems)> _starts = [];
    private readonly List<(Guid Id, int Processed)> _reports = [];
    private readonly List<Guid> _completes = [];

    public IReadOnlyList<(string Operation, int TotalItems)> Starts => _starts;
    public IReadOnlyList<(Guid Id, int Processed)> Reports => _reports;
    public IReadOnlyList<Guid> Completes => _completes;

    public Task<ProgressHandle> Start(string operation, int totalItems)
    {
        var handle = new ProgressHandle(Guid.NewGuid(), operation, totalItems);
        _starts.Add((operation, totalItems));

        return Task.FromResult(handle);
    }

    public Task Report(Guid id, int processed)
    {
        _reports.Add((id, processed));
        return Task.CompletedTask;
    }

    public Task Complete(Guid id)
    {
        _completes.Add(id);
        return Task.CompletedTask;
    }

    public Task ReportMessage(string message) => Task.CompletedTask;

    public Task ReportError(string message) => Task.CompletedTask;
}
