using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Octans.Core.Downloads;

namespace Octans.Server;

public sealed class DownloadManager(
    IDownloadQueue downloadQueue,
    DownloadProcessor processor,
    ILogger<DownloadManager> logger,
    DownloadManagerOptions options) : BackgroundService
{
    private readonly SemaphoreSlim _concurrencyLimiter = new(options.MaxConcurrentDownloads);
    private readonly int _maxConcurrentDownloads = options.MaxConcurrentDownloads;

    public override void Dispose()
    {
        _concurrencyLimiter.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Download Manager started with max concurrency: {Concurrency}", _maxConcurrentDownloads);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Only proceed if we have available slots
                await _concurrencyLimiter.WaitAsync(stoppingToken);

                // Get next eligible download
                var result = await downloadQueue.DequeueNextEligibleAsync(stoppingToken);

                if (result.Download != null)
                {
                    _ = processor.ProcessDownloadAsync(result.Download, stoppingToken)
                        .ContinueWith(_ => _concurrencyLimiter.Release(), TaskScheduler.Default);
                }
                else
                {
                    _concurrencyLimiter.Release();
                    var delay = result.SuggestedDelay ?? TimeSpan.FromSeconds(1);
                    await Task.Delay(delay, stoppingToken);
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
}