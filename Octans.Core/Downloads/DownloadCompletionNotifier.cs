using Octans.Data.Models;

namespace Octans.Core.Downloads;

public interface IDownloadCompletionNotifier
{
    Task DownloadCompletedAsync(DownloadStatus status, CancellationToken cancellationToken = default);
}

public sealed class NoOpDownloadCompletionNotifier : IDownloadCompletionNotifier
{
    public Task DownloadCompletedAsync(DownloadStatus status, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
