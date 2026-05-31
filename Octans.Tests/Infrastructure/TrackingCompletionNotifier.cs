using System.Collections.Concurrent;
using Octans.Core.Http;
using Octans.Core.Http.Models;

namespace Octans.Tests.Infrastructure;

internal sealed class TrackingCompletionNotifier : IDownloadCompletionNotifier
{
    private readonly ConcurrentQueue<DownloadJobResult> _finishedDownloads = new();

    public IReadOnlyCollection<DownloadJobResult> FinishedDownloads => _finishedDownloads.ToArray();

    public Task DownloadFinishedAsync(DownloadJobResult result, CancellationToken cancellationToken = default)
    {
        _finishedDownloads.Enqueue(result);
        return Task.CompletedTask;
    }
}
