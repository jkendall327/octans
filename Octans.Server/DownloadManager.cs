using System.Diagnostics;
using Octans.Core.Downloaders;
using Octans.Core.Downloads;

namespace Octans.Server;

public sealed class DownloadManager : BackgroundService
{
    private readonly SemaphoreSlim _concurrencyLimiter;
    private readonly IDownloadQueue _downloadQueue;
    private readonly IBandwidthLimiterService _bandwidthLimiter;
    private readonly DownloadStateService _stateService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DownloadManager> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly int _maxConcurrentDownloads;

    public DownloadManager(
        IDownloadQueue downloadQueue,
        IBandwidthLimiterService bandwidthLimiter,
        DownloadStateService stateService,
        IHttpClientFactory httpClientFactory,
        ILogger<DownloadManager> logger,
        DownloadManagerOptions options,
        IServiceScopeFactory scopeFactory)
    {
        _downloadQueue = downloadQueue;
        _bandwidthLimiter = bandwidthLimiter;
        _stateService = stateService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _maxConcurrentDownloads = options.MaxConcurrentDownloads;
        _concurrencyLimiter = new(options.MaxConcurrentDownloads);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Download Manager started with max concurrency: {Concurrency}", _maxConcurrentDownloads);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Only proceed if we have available slots
                await _concurrencyLimiter.WaitAsync(stoppingToken);
                
                // Get next eligible download
                var nextDownload = await _downloadQueue.DequeueNextEligibleAsync(stoppingToken);
                
                if (nextDownload != null)
                {
                    // Start download in background
                    _ = ProcessDownloadAsync(nextDownload, stoppingToken)
                        .ContinueWith(_ => _concurrencyLimiter.Release(), TaskScheduler.Default);
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
                _logger.LogError(ex, "Error in download manager loop");
                _concurrencyLimiter.Release();
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        _logger.LogInformation("Download Manager stopping");
    }

    private async Task ProcessDownloadAsync(QueuedDownload download, CancellationToken globalCancellation)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        var service = scope.ServiceProvider.GetRequiredService<IDownloadService>();
        
        var downloadId = download.Id;
        var downloadToken = service.GetDownloadToken(downloadId);
        
        // Create a combined token for this specific download
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(globalCancellation, downloadToken);
        var combinedToken = linkedCts.Token;
        
        try
        {
            _stateService.UpdateState(downloadId, DownloadState.InProgress);
            _logger.LogInformation("Starting download: {Url} -> {Path}", download.Url, download.DestinationPath);
            
            Directory.CreateDirectory(Path.GetDirectoryName(download.DestinationPath) ?? throw new InvalidOperationException());
            
            using var httpClient = _httpClientFactory.CreateClient("DownloadClient");
            httpClient.Timeout = TimeSpan.FromHours(2); // Long timeout for large files
            
            using var response = await httpClient.GetAsync(
                download.Url, 
                HttpCompletionOption.ResponseHeadersRead, 
                combinedToken);
            
            response.EnsureSuccessStatusCode();
            
            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            _stateService.UpdateProgress(downloadId, 0, totalBytes, 0);

            await using var contentStream = await response.Content.ReadAsStreamAsync(combinedToken);
            await using var fileStream = new FileStream(download.DestinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            
            var buffer = new byte[81920]; // 80 KB buffer
            long bytesDownloaded = 0;
            var sw = Stopwatch.StartNew();
            long lastReportTime = 0;
            var lastReportBytes = 0L;
            
            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, combinedToken)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, combinedToken);
                
                bytesDownloaded += bytesRead;
                
                // Report progress every 100ms
                if (sw.ElapsedMilliseconds - lastReportTime > 100)
                {
                    // Calculate speed based on bytes downloaded since last report
                    var timeDelta = sw.ElapsedMilliseconds - lastReportTime;
                    var bytesDelta = bytesDownloaded - lastReportBytes;
                    var speed = bytesDelta / (timeDelta / 1000.0);
                    
                    _stateService.UpdateProgress(downloadId, bytesDownloaded, totalBytes, speed);
                    
                    lastReportTime = sw.ElapsedMilliseconds;
                    lastReportBytes = bytesDownloaded;
                }
            }
            
            // Final progress update and state change
            _stateService.UpdateProgress(downloadId, bytesDownloaded, totalBytes, bytesDownloaded / sw.Elapsed.TotalSeconds);
            _stateService.UpdateState(downloadId, DownloadState.Completed);
            
            // Record bandwidth usage
            _bandwidthLimiter.RecordDownload(download.Domain, bytesDownloaded);
            
            _logger.LogInformation("Download completed: {Url} -> {Path}, {Bytes} bytes", 
                download.Url, download.DestinationPath, bytesDownloaded);
        }
        catch (OperationCanceledException) when (combinedToken.IsCancellationRequested && !globalCancellation.IsCancellationRequested)
        {
            _logger.LogInformation("Download canceled: {Url}", download.Url);
            _stateService.UpdateState(downloadId, DownloadState.Canceled);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !globalCancellation.IsCancellationRequested)
        {
            _logger.LogError(ex, "Download failed: {Url}", download.Url);
            _stateService.UpdateState(downloadId, DownloadState.Failed, ex.Message);
        }
    }
}