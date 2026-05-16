using Octans.Core.Http.Models;

namespace Octans.Core.Http;

/// <summary>
/// Extension point for feature-specific reactions to completed, failed, or
/// canceled download jobs.
/// </summary>
public interface IDownloadCompletionNotifier
{
    Task DownloadFinishedAsync(DownloadJobResult result, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default notifier used when no feature has registered download completion work.
/// </summary>
public sealed class NoOpDownloadCompletionNotifier : IDownloadCompletionNotifier
{
    public Task DownloadFinishedAsync(DownloadJobResult result, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
