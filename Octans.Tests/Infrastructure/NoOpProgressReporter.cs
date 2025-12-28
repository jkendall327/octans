using Octans.Core.Progress;

namespace Octans.Tests;

public sealed class NoOpProgressReporter : IBackgroundProgressReporter
{
    public Task<ProgressHandle> Start(string operation, int totalItems) =>
        Task.FromResult(new ProgressHandle(Guid.NewGuid(), operation, totalItems));

    public Task Report(Guid id, int processed) => Task.CompletedTask;

    public Task Complete(Guid id) => Task.CompletedTask;

    public Task ReportMessage(string message) => Task.CompletedTask;

    public Task ReportError(string message) => Task.CompletedTask;
}