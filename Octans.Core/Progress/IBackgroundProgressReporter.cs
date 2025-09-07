namespace Octans.Core.Progress;

public interface IBackgroundProgressReporter
{
    Task<ProgressHandle> Start(string operation, int totalItems);
    Task Report(Guid id, int processed);
    Task Complete(Guid id);
    Task ReportMessage(string message);
    Task ReportError(string message);
}
