using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Octans.Core.Downloads;

public interface IActiveDownloadRegistry
{
    CancellationToken GetToken(Guid downloadId);
    void Cancel(Guid downloadId);
    Task CancelAllAsync();
}

public sealed class ActiveDownloadRegistry(
    ILogger<ActiveDownloadRegistry> logger) : IActiveDownloadRegistry, IDisposable, IAsyncDisposable
{
    private readonly CancellationTokenSource _globalCancellation = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _downloadCancellations = new();

    public CancellationToken GetToken(Guid downloadId)
    {
        if (_downloadCancellations.TryGetValue(downloadId, out var cts))
        {
            logger.LogDebug("Reusing existing cancellation token for download {DownloadId}", downloadId);
            return cts.Token;
        }

        logger.LogDebug("Creating new cancellation token for download {DownloadId}", downloadId);

        cts = CancellationTokenSource.CreateLinkedTokenSource(_globalCancellation.Token);
        _downloadCancellations[downloadId] = cts;

        return cts.Token;
    }

    public void Cancel(Guid downloadId)
    {
        if (!_downloadCancellations.TryRemove(downloadId, out var cts))
        {
            logger.LogDebug("No active cancellation token found for download {DownloadId}", downloadId);
            return;
        }

        logger.LogDebug("Canceling download token for {DownloadId}", downloadId);

        cts.Cancel();
        cts.Dispose();
    }

    public async Task CancelAllAsync()
    {
        logger.LogInformation("Canceling all active downloads");

        await _globalCancellation.CancelAsync();

        foreach ((_, var cts) in _downloadCancellations)
        {
            await cts.CancelAsync();
            cts.Dispose();
        }

        _downloadCancellations.Clear();
    }

    public void Dispose()
    {
        _globalCancellation.Cancel();
        _globalCancellation.Dispose();

        foreach (var cts in _downloadCancellations.Values)
        {
            cts.Dispose();
        }

        _downloadCancellations.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await _globalCancellation.CancelAsync();
        _globalCancellation.Dispose();

        foreach (var cts in _downloadCancellations.Values)
        {
            cts.Dispose();
        }

        _downloadCancellations.Clear();
    }
}
