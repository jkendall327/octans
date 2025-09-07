namespace Octans.Core.Progress;

public interface IBackgroundProgressReporter
{
    ProgressHandle Start(string operation, int totalItems);
    void Report(Guid id, int processed);
    void Complete(Guid id);
    void ReportMessage(string message);
    void ReportError(string message);
}
