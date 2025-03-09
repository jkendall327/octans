using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Octans.Core.Downloaders;

namespace Octans.Core.Downloads;

/// <summary>
/// Handles the actual HTTP machinery of downloading content.
/// </summary>
public class DownloadProcessor(
    IBandwidthLimiter bandwidthLimiter,
    DownloadStateService stateService,
    IDownloadService downloadService,
    IHttpClientFactory httpClientFactory,
    ILogger<DownloadProcessor> logger)
{
    public async Task ProcessDownloadAsync(QueuedDownload download, CancellationToken globalCancellation)
    {
        var downloadId = download.Id;
        var downloadToken = downloadService.GetDownloadToken(downloadId);

        // Create a combined token for this specific download
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(globalCancellation, downloadToken);
        var combinedToken = linkedCts.Token;

        try
        {
            await ProcessCore(download, downloadId, combinedToken);
        }
        catch (OperationCanceledException)
            when (combinedToken.IsCancellationRequested && !globalCancellation.IsCancellationRequested)
        {
            logger.LogInformation("Download canceled: {Url}", download.Url);
            stateService.UpdateState(downloadId, DownloadState.Canceled);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !globalCancellation.IsCancellationRequested)
        {
            logger.LogError(ex, "Download failed: {Url}", download.Url);
            stateService.UpdateState(downloadId, DownloadState.Failed, ex.Message);
        }
    }

    private async Task ProcessCore(QueuedDownload download, Guid downloadId, CancellationToken combinedToken)
    {
        stateService.UpdateState(downloadId, DownloadState.InProgress);
        logger.LogInformation("Starting download: {Url} -> {Path}", download.Url, download.DestinationPath);

        var directoryName = Path.GetDirectoryName(download.DestinationPath) ??
                            throw new InvalidOperationException();

        Directory.CreateDirectory(directoryName);

        using var httpClient = httpClientFactory.CreateClient("DownloadClient");
        httpClient.Timeout = TimeSpan.FromHours(2); // Long timeout for large files

        using var response = await httpClient.GetAsync(
            new Uri(download.Url),
            HttpCompletionOption.ResponseHeadersRead,
            combinedToken);

        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        stateService.UpdateProgress(downloadId, 0, totalBytes, 0);

        await using var contentStream = await response.Content.ReadAsStreamAsync(combinedToken);
        await using var fileStream = new FileStream(download.DestinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            true);

        var buffer = new byte[81920]; // 80 KB buffer
        long bytesDownloaded = 0;
        var sw = Stopwatch.StartNew();
        long lastReportTime = 0;
        var lastReportBytes = 0L;

        int bytesRead;
        while ((bytesRead = await contentStream.ReadAsync(buffer, combinedToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), combinedToken);

            bytesDownloaded += bytesRead;

            // Report progress every 100ms
            if (sw.ElapsedMilliseconds - lastReportTime <= 100) continue;

            // Calculate speed based on bytes downloaded since last report
            var timeDelta = sw.ElapsedMilliseconds - lastReportTime;
            var bytesDelta = bytesDownloaded - lastReportBytes;
            var speed = bytesDelta / (timeDelta / 1000.0);

            stateService.UpdateProgress(downloadId, bytesDownloaded, totalBytes, speed);

            lastReportTime = sw.ElapsedMilliseconds;
            lastReportBytes = bytesDownloaded;
        }

        // Final progress update and state change
        stateService.UpdateProgress(downloadId,
            bytesDownloaded,
            totalBytes,
            bytesDownloaded / sw.Elapsed.TotalSeconds);
        stateService.UpdateState(downloadId, DownloadState.Completed);

        // Record bandwidth usage
        bandwidthLimiter.RecordDownload(download.Domain, bytesDownloaded);

        logger.LogInformation("Download completed: {Url} -> {Path}, {Bytes} bytes",
            download.Url,
            download.DestinationPath,
            bytesDownloaded);
    }
}