using Octans.Core.Downloads.Models;

namespace Octans.Core.Downloads;

public interface IDownloadCompletionNotifier
{
    Task DownloadFinishedAsync(DownloadJobResult result, CancellationToken cancellationToken = default);
}

public sealed class NoOpDownloadCompletionNotifier : IDownloadCompletionNotifier
{
    public Task DownloadFinishedAsync(DownloadJobResult result, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
