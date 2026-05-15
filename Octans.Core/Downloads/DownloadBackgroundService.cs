using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Octans.Core.Downloads.Models;
using Octans.Data.Models;

namespace Octans.Core.Downloads;

public sealed class DownloadBackgroundService(
    IDownloadQueue downloadQueue,
    IDownloadStateService stateService,
    HttpDownloader processor,
    DownloadStagingPaths stagingPaths,
    ILogger<DownloadBackgroundService> logger,
    DownloadManagerOptions options) : BackgroundService
{
    private readonly SemaphoreSlim _concurrencyLimiter = new(options.MaxConcurrentDownloads);
    private readonly Lock _activeDomainsLock = new();
    private readonly Dictionary<string, int> _activeDomainCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _maxConcurrentDownloads = options.MaxConcurrentDownloads;
    private readonly int _maxConcurrentDownloadsPerDomain = options.MaxConcurrentDownloadsPerDomain;

    public override void Dispose()
    {
        _concurrencyLimiter.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Download Manager started with max concurrency: {Concurrency}", _maxConcurrentDownloads);
        await stateService.InitializeFromDbAsync();
        await RestoreInterruptedDownloads(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Only proceed if we have available slots
                await _concurrencyLimiter.WaitAsync(stoppingToken);

                // Get next eligible download
                var nextDownload = await downloadQueue.DequeueNextEligibleAsync(
                    stoppingToken,
                    GetSaturatedDomains());

                if (nextDownload != null)
                {
                    TrackDomainStarted(nextDownload.Domain);
                    _ = ProcessDownload(nextDownload, stoppingToken);
                }
                else
                {
                    // No downloads ready, release semaphore and wait a bit
                    _concurrencyLimiter.Release();
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown, exit gracefully
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in download manager loop");
                _concurrencyLimiter.Release();
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        logger.LogInformation("Download Manager stopping");
    }

    private async Task RestoreInterruptedDownloads(CancellationToken stoppingToken)
    {
        var downloads = stateService.GetAllDownloads()
            .Where(d => d.State is DownloadState.Queued or DownloadState.WaitingForBandwidth or DownloadState.InProgress)
            .ToList();

        foreach (var download in downloads)
        {
            DeleteStagingFileBestEffort(download);
            await downloadQueue.EnsureQueuedAsync(download, stoppingToken);

            if (download.State is DownloadState.InProgress or DownloadState.WaitingForBandwidth)
            {
                await stateService.UpdateState(download.Id, DownloadState.Queued);
            }
        }
    }

    private async Task ProcessDownload(QueuedDownload download, CancellationToken stoppingToken)
    {
        try
        {
            await processor.ProcessDownloadAsync(download, stoppingToken);
        }
        finally
        {
            TrackDomainFinished(download.Domain);
            _concurrencyLimiter.Release();
        }
    }

    private HashSet<string>? GetSaturatedDomains()
    {
        if (_maxConcurrentDownloadsPerDomain <= 0)
        {
            return null;
        }

        lock (_activeDomainsLock)
        {
            return _activeDomainCounts
                .Where(kvp => kvp.Value >= _maxConcurrentDownloadsPerDomain)
                .Select(kvp => kvp.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void TrackDomainStarted(string domain)
    {
        if (_maxConcurrentDownloadsPerDomain <= 0)
        {
            return;
        }

        lock (_activeDomainsLock)
        {
            _activeDomainCounts.TryGetValue(domain, out var count);
            _activeDomainCounts[domain] = count + 1;
        }
    }

    private void TrackDomainFinished(string domain)
    {
        if (_maxConcurrentDownloadsPerDomain <= 0)
        {
            return;
        }

        lock (_activeDomainsLock)
        {
            if (!_activeDomainCounts.TryGetValue(domain, out var count))
            {
                return;
            }

            if (count <= 1)
            {
                _activeDomainCounts.Remove(domain);
                return;
            }

            _activeDomainCounts[domain] = count - 1;
        }
    }

    private void DeleteStagingFileBestEffort(DownloadStatus download)
    {
        try
        {
            stagingPaths.DeleteStagingFile(download.Id, download.DestinationPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete stale staging file for download {DownloadId}", download.Id);
        }
    }
}
