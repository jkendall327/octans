namespace Octans.Data.Models;

public enum DownloadTerminalOutcome
{
    Completed,
    Failed,
    Canceled,
    NotModified,
    AlreadySatisfied,
    ValidationFailed,
    TerminalHttpFailure
}
