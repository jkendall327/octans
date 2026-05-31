using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octans.Core.Http.Models;
using Octans.Data.Models;

namespace Octans.Core.Http;

/// <summary>
/// Hosted worker that drains the durable download queue while enforcing global
/// and per-domain concurrency limits.
/// </summary>
internal sealed class DownloadBackgroundService(
    IDownloadQueue downloadQueue,
    IDownloadStateService stateService,
    HttpDownloader processor,
    DownloadStagingPaths stagingPaths,
    IDownloadHostCircuitRegistry hostCircuitRegistry,
    ILogger<DownloadBackgroundService> logger,
    IOptions<DownloadManagerOptions> options) : BackgroundService
{
    private readonly SemaphoreSlim _concurrencyLimiter = new(options.Value.MaxConcurrentDownloads);
    private readonly Lock _activeDomainsLock = new();
    private readonly Dictionary<string, int> _activeDomainCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _maxConcurrentDownloads = options.Value.MaxConcurrentDownloads;
    private readonly int _maxConcurrentDownloadsPerDomain = options.Value.MaxConcurrentDownloadsPerDomain;

    public override void Dispose()
    {
        _concurrencyLimiter.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Download Manager started with max concurrency {Concurrency} and per-domain concurrency {PerDomainConcurrency}",
            _maxConcurrentDownloads,
            _maxConcurrentDownloadsPerDomain);
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
                    GetUnavailableDomains());

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

        if (downloads.Count > 0)
        {
            logger.LogInformation("Restoring {DownloadCount} interrupted downloads", downloads.Count);
        }

        foreach (var download in downloads)
        {
            using var scope = logger.BeginScope(new Dictionary<string, object?>
            {
                ["DownloadId"] = download.Id,
                ["Domain"] = download.Domain,
                ["PreviousState"] = download.State
            });

            DeleteStagingFileBestEffort(download);
            await downloadQueue.EnsureQueuedAsync(download, stoppingToken);

            if (download.State is DownloadState.InProgress or DownloadState.WaitingForBandwidth)
            {
                logger.LogInformation("Resetting interrupted download to queued state");
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

    private HashSet<string>? GetUnavailableDomains()
    {
        var openDomains = hostCircuitRegistry.GetOpenDomains();
        var saturatedDomains = GetSaturatedDomains();

        if (saturatedDomains is null)
        {
            return openDomains.Count == 0
                ? null
                : openDomains.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        saturatedDomains.UnionWith(openDomains);
        return saturatedDomains.Count == 0 ? null : saturatedDomains;
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
            logger.LogDebug(
                "Tracked active domain download: {Domain} now has {ActiveDownloadCount} active downloads",
                domain,
                _activeDomainCounts[domain]);
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
                logger.LogDebug("Cleared active domain download count for {Domain}", domain);
                return;
            }

            _activeDomainCounts[domain] = count - 1;
            logger.LogDebug(
                "Released active domain download: {Domain} now has {ActiveDownloadCount} active downloads",
                domain,
                _activeDomainCounts[domain]);
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
