using System.Globalization;
using System.IO.Abstractions;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octans.Core.Downloads.Bandwidth;
using Octans.Core.Downloads.Models;
using Octans.Data.Models;
using Polly.CircuitBreaker;

namespace Octans.Core.Downloads;

/// <summary>
/// Handles the actual HTTP machinery of downloading content.
/// </summary>
public class HttpDownloader(
    IDownloadBandwidthGate bandwidthGate,
    IDownloadDiskSpaceGuard diskSpaceGuard,
    IDownloadStateService stateService,
    IDownloadLifecycleService lifecycle,
    IActiveDownloadRegistry activeDownloads,
    IInFlightDownloadCoordinator inFlightDownloads,
    IDownloadHostCircuitRegistry hostCircuitRegistry,
    IHttpClientFactory httpClientFactory,
    IDownloadRequestHeaderProvider requestHeaderProvider,
    IFileSystem fileSystem,
    DownloadStagingPaths stagingPaths,
    TimeProvider timeProvider,
    IOptions<DownloadManagerOptions> options,
    ILogger<HttpDownloader> logger)
{
    public async Task ProcessDownloadAsync(QueuedDownload download, CancellationToken globalCancellation)
    {
        var downloadId = download.Id;
        var downloadToken = activeDownloads.GetToken(downloadId);

        // Create a combined token for this specific download
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(globalCancellation, downloadToken);
        var combinedToken = linkedCts.Token;

        try
        {
            await ProcessCore(download, downloadId, globalCancellation, downloadToken);
        }
        catch (OperationCanceledException)
            when (combinedToken.IsCancellationRequested && !globalCancellation.IsCancellationRequested)
        {
            logger.LogInformation("Download canceled: {Url}", download.Url);
            await lifecycle.MarkCanceledAsync(downloadId);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is { } statusCode)
        {
            logger.LogWarning(ex, "Download failed with HTTP status {StatusCode}: {Url}", (int)statusCode, download.Url);
            await lifecycle.MarkFailedAsync(
                downloadId,
                ex.Message,
                DownloadFailureCategory.Http,
                DownloadTerminalOutcome.TerminalHttpFailure,
                (int)statusCode);
        }
        catch (BrokenCircuitException ex)
        {
            var message = hostCircuitRegistry.TryGetOpenCircuit(download.Domain, out var openUntil)
                ? $"Host circuit for {download.Domain} is open until {openUntil:u}."
                : $"Host circuit for {download.Domain} is open.";

            logger.LogWarning(ex, "Download skipped because host circuit is open: {Url}", download.Url);
            await lifecycle.MarkFailedAsync(
                downloadId,
                message,
                DownloadFailureCategory.Network);
        }
        catch (MissingDownloadCredentialsException ex)
        {
            logger.LogWarning(ex, "Download cannot start because required credentials are missing: {Url}", download.Url);
            await lifecycle.MarkFailedAsync(
                downloadId,
                ex.Message,
                DownloadFailureCategory.Authentication,
                DownloadTerminalOutcome.ValidationFailed,
                validationMessage: ex.Message);
        }
        catch (DownloadContentTypeException ex)
        {
            logger.LogWarning(ex, "Download content type validation failed: {Url}", download.Url);
            await lifecycle.MarkFailedAsync(
                downloadId,
                ex.Message,
                DownloadFailureCategory.Validation,
                DownloadTerminalOutcome.ValidationFailed,
                validationMessage: ex.Message,
                responseContentType: ex.ResponseContentType);
        }
        catch (DownloadSizeLimitException ex)
        {
            logger.LogWarning(ex, "Download size limit validation failed: {Url}", download.Url);
            await lifecycle.MarkFailedAsync(
                downloadId,
                ex.Message,
                DownloadFailureCategory.SizeLimit,
                DownloadTerminalOutcome.ValidationFailed,
                validationMessage: ex.Message);
        }
        catch (InvalidDataException ex)
        {
            logger.LogWarning(ex, "Download validation failed: {Url}", download.Url);
            await lifecycle.MarkFailedAsync(
                downloadId,
                ex.Message,
                DownloadFailureCategory.Validation,
                DownloadTerminalOutcome.ValidationFailed,
                validationMessage: ex.Message);
        }
        catch (DownloadDiskSpaceException ex)
        {
            logger.LogWarning(ex, "Download failed because disk space was insufficient: {Url}", download.Url);
            await lifecycle.MarkFailedAsync(
                downloadId,
                ex.Message,
                DownloadFailureCategory.Filesystem);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !globalCancellation.IsCancellationRequested)
        {
            logger.LogError(ex, "Download failed: {Url}", download.Url);
            await lifecycle.MarkFailedAsync(
                downloadId,
                ex.Message,
                CategorizeFailure(ex));
        }
    }

    private async Task ProcessCore(
        QueuedDownload download,
        Guid downloadId,
        CancellationToken globalCancellation,
        CancellationToken downloadToken)
    {
        var started = await lifecycle.MarkInProgressAsync(downloadId);
        if (!started)
        {
            logger.LogDebug("Skipping download because it is no longer queued: {Url}", download.Url);
            return;
        }

        logger.LogInformation("Starting download: {Url} -> {Path}", download.Url, download.DestinationPath);

        var deduplicationKey = GetDeduplicationKey(download);
        var stagingPath = stagingPaths.GetSharedStagingPath(download, deduplicationKey);
        await using var lease = inFlightDownloads.JoinOrStart(
            deduplicationKey,
            stagingPath,
            (_, transferCancellation) => TransferToSharedStagingFile(download, deduplicationKey, globalCancellation, transferCancellation));

        try
        {
            var result = await lease.TransferTask.WaitAsync(downloadToken);
            await FinalizeSharedDownloadAsync(download, result, lease, downloadToken);
        }
        catch (OperationCanceledException) when (downloadToken.IsCancellationRequested && !globalCancellation.IsCancellationRequested)
        {
            lease.ParticipantCanceled();
            throw;
        }
    }

    private async Task<SharedDownloadResult> TransferToSharedStagingFile(
        QueuedDownload download,
        string deduplicationKey,
        CancellationToken globalCancellation,
        CancellationToken transferCancellation)
    {
        var stagingPath = stagingPaths.PrepareFreshSharedStagingPath(download, deduplicationKey);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(globalCancellation, transferCancellation);
        var combinedToken = linkedCts.Token;

        using var httpClient = httpClientFactory.CreateClient("DownloadClient");
        httpClient.Timeout = TimeSpan.FromHours(2); // Long timeout for large files

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(download.Url));
            requestHeaderProvider.ApplyHeaders(request);

            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                combinedToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"HTTP download failed with status {(int)response.StatusCode} ({response.ReasonPhrase ?? response.StatusCode.ToString()}).",
                    null,
                    response.StatusCode);
            }

            var contentTypeValidation = DownloadContentTypeValidator.Validate(
                download,
                response.Content.Headers.ContentType,
                options.Value.ContentTypeValidation);
            if (!contentTypeValidation.Accepted)
            {
                throw new DownloadContentTypeException(
                    contentTypeValidation.Message ?? "Download content type did not match expectations.",
                    contentTypeValidation.ResponseContentType);
            }

            _ = DownloadHashValidator.Create(download.ExpectedHashes);

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            var maxDownloadSizeBytes = GetMaxDownloadSizeBytes(download);
            if (totalBytes >= 0 && maxDownloadSizeBytes is { } maxBytes && totalBytes > maxBytes)
            {
                throw DownloadSizeLimitException.ForReportedSize(totalBytes, maxBytes);
            }

            if (totalBytes >= 0)
            {
                diskSpaceGuard.EnsureSufficientSpace(stagingPath, totalBytes);
            }

            var (bytesDownloaded, startTime) = await StreamToStagingFile(
                download,
                stagingPath,
                response,
                maxDownloadSizeBytes,
                combinedToken);
            if (totalBytes >= 0 && bytesDownloaded != totalBytes)
            {
                throw new InvalidDataException(
                    $"Content-Length mismatch: download ended after {bytesDownloaded} bytes, but the server reported {totalBytes} bytes.");
            }

            return new(
                stagingPath,
                bytesDownloaded,
                totalBytes,
                timeProvider.GetElapsedTime(startTime),
                (int)response.StatusCode,
                response.Content.Headers.ContentType?.ToString(),
                response.Headers.ETag?.ToString(),
                response.Content.Headers.LastModified);
        }
        catch
        {
            DeleteStagingFileBestEffort(stagingPath);
            throw;
        }
    }

    private async Task FinalizeSharedDownloadAsync(
        QueuedDownload download,
        SharedDownloadResult result,
        InFlightDownloadLease lease,
        CancellationToken downloadToken)
    {
        var parsedContentType = System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(
            result.ResponseContentType,
            out var mediaTypeHeader)
            ? mediaTypeHeader
            : null;
        var contentTypeValidation = DownloadContentTypeValidator.Validate(
            download,
            parsedContentType,
            options.Value.ContentTypeValidation);
        if (!contentTypeValidation.Accepted)
        {
            throw new DownloadContentTypeException(
                contentTypeValidation.Message ?? "Download content type did not match expectations.",
                contentTypeValidation.ResponseContentType);
        }

        var hashValidators = DownloadHashValidator.Create(download.ExpectedHashes);
        hashValidators.Validate(fileSystem, result.StagingPath);

        if (result.TotalBytes >= 0)
        {
            diskSpaceGuard.EnsureSufficientSpace(download.DestinationPath, result.TotalBytes);
        }

        await lease.RunFinalizerAsync(() => CopySharedStagingToDestinationAsync(download, result), downloadToken);

        await stateService.UpdateProgress(download.Id,
            result.BytesDownloaded,
            result.TotalBytes,
            result.Elapsed.TotalSeconds <= 0 ? 0 : result.BytesDownloaded / result.Elapsed.TotalSeconds);

        await lifecycle.MarkCompletedAsync(download.Id, new()
        {
            Outcome = DownloadTerminalOutcome.Completed,
            HttpStatusCode = result.HttpStatusCode,
            ResponseContentType = result.ResponseContentType,
            ResponseETag = result.ResponseETag,
            ResponseLastModified = result.ResponseLastModified
        });

        logger.LogInformation("Download completed: {Url} -> {Path}, {Bytes} bytes",
            download.Url,
            download.DestinationPath,
            result.BytesDownloaded);
    }

    private async Task CopySharedStagingToDestinationAsync(QueuedDownload download, SharedDownloadResult result)
    {
        var destinationDirectory = fileSystem.Path.GetDirectoryName(download.DestinationPath) ??
                                   throw new InvalidOperationException("Download destination must include a directory.");
        fileSystem.Directory.CreateDirectory(destinationDirectory);

        try
        {
            await using var source = fileSystem.File.OpenRead(result.StagingPath);
            await using var destination = fileSystem.File.Create(download.DestinationPath);
            await source.CopyToAsync(destination);
        }
        catch (IOException ex)
        {
            throw new DownloadDiskSpaceException(
                "Download failed while writing to disk. The destination may not have enough free space.",
                ex);
        }
    }

    private async Task<(long BytesDownloaded, long StartTime)> StreamToStagingFile(
        QueuedDownload download,
        string stagingPath,
        HttpResponseMessage response,
        long? maxDownloadSizeBytes,
        CancellationToken combinedToken)
    {
        await using var contentStream = await response.Content.ReadAsStreamAsync(combinedToken);
        await using var fileStream = fileSystem.File.Create(stagingPath,
            81920,
            FileOptions.Asynchronous);

        var buffer = new byte[81920]; // 80 KB buffer
        long bytesDownloaded = 0;
        var startTime = timeProvider.GetTimestamp();
        double lastReportTime = 0;
        var lastReportBytes = 0L;

        int bytesRead;
        while ((bytesRead = await contentStream.ReadAsync(buffer, combinedToken)) > 0)
        {
            if (maxDownloadSizeBytes is { } maxBytes && bytesDownloaded > maxBytes - bytesRead)
            {
                throw DownloadSizeLimitException.ForReceivedSize(bytesDownloaded + bytesRead, maxBytes);
            }

            await bandwidthGate.WaitForBytesAsync(download.Domain, bytesRead, combinedToken);
            diskSpaceGuard.EnsureSufficientSpace(
                stagingPath,
                GetRequiredBytesBeforeWrite(response.Content.Headers.ContentLength, bytesDownloaded, bytesRead));

            try
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), combinedToken);
            }
            catch (IOException ex)
            {
                throw new DownloadDiskSpaceException(
                    "Download failed while writing to disk. The destination may not have enough free space.",
                    ex);
            }

            bytesDownloaded += bytesRead;

            // Get current elapsed time in milliseconds
            var currentElapsedMs = timeProvider.GetElapsedTime(startTime).TotalMilliseconds;

            // Report progress every 100ms
            if (currentElapsedMs - lastReportTime <= 100) continue;

            // Calculate speed based on bytes downloaded since last report
            var timeDelta = currentElapsedMs - lastReportTime;
            var bytesDelta = bytesDownloaded - lastReportBytes;
            var speed = bytesDelta / (timeDelta / 1000.0);

            await stateService.UpdateProgress(
                download.Id,
                bytesDownloaded,
                response.Content.Headers.ContentLength ?? -1,
                speed);

            lastReportTime = currentElapsedMs;
            lastReportBytes = bytesDownloaded;
        }

        return (bytesDownloaded, startTime);
    }


    private sealed class DownloadHashValidator
    {
        private static readonly Dictionary<string, Func<Stream, byte[]>> SupportedAlgorithms = new(StringComparer.OrdinalIgnoreCase)
        {
            ["SHA256"] = stream => SHA256.HashData(stream),
            ["SHA384"] = stream => SHA384.HashData(stream),
            ["SHA512"] = stream => SHA512.HashData(stream)
        };

        private readonly List<HashValidationState> _states;

        private DownloadHashValidator(List<HashValidationState> states)
        {
            _states = states;
        }

        public static DownloadHashValidator Create(string? serializedExpectations)
        {
            var expectations = DownloadHashExpectations.Deserialize(serializedExpectations);
            var states = expectations.Select(BuildState).ToList();
            return new(states);
        }

        public void Validate(IFileSystem fileSystem, string stagingPath)
        {
            foreach (var state in _states)
            {
                using var stream = fileSystem.File.OpenRead(stagingPath);
                var actual = Convert.ToHexString(state.ComputeHash(stream)).ToLower(CultureInfo.InvariantCulture);
                if (string.Equals(actual, state.ExpectedHex, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                throw new InvalidDataException(
                    $"Hash mismatch for {state.DisplayAlgorithm}: expected {state.ExpectedHex}, got {actual}.");
            }
        }

        private static HashValidationState BuildState(DownloadHashExpectation expectation)
        {
            if (string.IsNullOrWhiteSpace(expectation.Algorithm))
            {
                throw new InvalidDataException("Missing hash validator algorithm.");
            }

            var algorithmKey = NormalizeAlgorithm(expectation.Algorithm);
            if (!SupportedAlgorithms.TryGetValue(algorithmKey, out var computeHash))
            {
                throw new InvalidDataException(
                    $"Unsupported hash validator '{expectation.Algorithm}'. Supported validators: SHA-256, SHA-384, SHA-512.");
            }

            var expectedHex = NormalizeHex(expectation.Value);
            if (expectedHex.Length == 0)
            {
                throw new InvalidDataException($"Missing expected hash value for {expectation.Algorithm}.");
            }

            if (!HasExpectedLength(algorithmKey, expectedHex))
            {
                throw new InvalidDataException(
                    $"Expected hash value for {expectation.Algorithm} has an invalid length.");
            }

            return new(expectation.Algorithm, expectedHex, computeHash);
        }

        private static string NormalizeAlgorithm(string algorithm)
        {
            return algorithm
                .Replace("-", string.Empty, StringComparison.Ordinal)
                .Replace("_", string.Empty, StringComparison.Ordinal)
                .Replace(" ", string.Empty, StringComparison.Ordinal)
                .ToUpperInvariant();
        }

        private static string NormalizeHex(string value)
        {
            var normalized = value
                .Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace(":", string.Empty, StringComparison.Ordinal)
                .ToLower(CultureInfo.InvariantCulture);

            if (normalized.Length % 2 != 0 || normalized.Any(c => !Uri.IsHexDigit(c)))
            {
                throw new InvalidDataException("Expected hash value must be hexadecimal.");
            }

            return normalized;
        }

        private static bool HasExpectedLength(string algorithmKey, string expectedHex)
        {
            var expectedLength = algorithmKey switch
            {
                "SHA256" => 64,
                "SHA384" => 96,
                "SHA512" => 128,
                _ => 0
            };

            return expectedHex.Length == expectedLength;
        }

        private sealed record HashValidationState(
            string DisplayAlgorithm,
            string ExpectedHex,
            Func<Stream, byte[]> ComputeHash);
    }

    private long? GetMaxDownloadSizeBytes(QueuedDownload download)
    {
        var sizeLimits = options.Value.SizeLimits;
        if (!sizeLimits.Enabled)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(download.SourceType) &&
            sizeLimits.MaxBytesBySourceType.TryGetValue(download.SourceType, out var sourceMaxBytes) &&
            sourceMaxBytes > 0)
        {
            return sourceMaxBytes;
        }

        if (sizeLimits.MaxBytesByDomain.TryGetValue(download.Domain, out var domainMaxBytes) &&
            domainMaxBytes > 0)
        {
            return domainMaxBytes;
        }

        return sizeLimits.MaxBytes > 0 ? sizeLimits.MaxBytes : null;
    }

    private static long GetRequiredBytesBeforeWrite(long? contentLength, long bytesDownloaded, int bytesRead)
    {
        if (contentLength is not { } totalBytes || totalBytes < 0)
        {
            return bytesRead;
        }

        return Math.Max(bytesRead, totalBytes - bytesDownloaded);
    }

    private void DeleteStagingFileBestEffort(Guid downloadId, string destinationPath)
    {
        try
        {
            stagingPaths.DeleteStagingFile(downloadId, destinationPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete staging file for {DestinationPath}", destinationPath);
        }
    }

    private void DeleteStagingFileBestEffort(string stagingPath)
    {
        try
        {
            if (fileSystem.File.Exists(stagingPath))
            {
                fileSystem.File.Delete(stagingPath);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete staging file {StagingPath}", stagingPath);
        }
    }


    private string GetDeduplicationKey(QueuedDownload download)
    {
        if (!string.IsNullOrWhiteSpace(download.RequestFingerprint))
        {
            return download.RequestFingerprint;
        }

        var uri = new Uri(download.Url);
        return requestHeaderProvider.GetRequestFingerprint(NormalizeUri(uri));
    }

    private static Uri NormalizeUri(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            Host = uri.IdnHost.ToLowerInvariant()
        };

        if (uri.IsDefaultPort)
        {
            builder.Port = -1;
        }

        return builder.Uri;
    }

    private static DownloadFailureCategory CategorizeFailure(Exception ex)
    {
        return ex switch
        {
            HttpRequestException => DownloadFailureCategory.Network,
            IOException => DownloadFailureCategory.Filesystem,
            UnauthorizedAccessException => DownloadFailureCategory.Filesystem,
            _ => DownloadFailureCategory.Unknown
        };
    }
}
