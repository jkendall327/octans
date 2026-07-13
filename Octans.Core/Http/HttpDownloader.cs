using System.Diagnostics;
using System.Globalization;
using System.IO.Abstractions;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octans.Core.Http.Bandwidth;
using Octans.Core.Http.Models;
using Octans.Data.Models;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Octans.Core.Http;

/// <summary>
/// Streams queued HTTP downloads into staging files, validates the response and
/// final payload, then commits successful downloads to their destination path.
/// </summary>
internal sealed class HttpDownloader(
    IDownloadBandwidthGate bandwidthGate,
    IDownloadDiskSpaceGuard diskSpaceGuard,
    IDownloadStateService stateService,
    IDownloadLifecycleService lifecycle,
    IActiveDownloadRegistry activeDownloads,
    IDownloadHostCircuitRegistry hostCircuitRegistry,
    IHttpClientFactory httpClientFactory,
    IDownloadRequestHeaderProvider requestHeaderProvider,
    IFileSystem fileSystem,
    DownloadStagingPaths stagingPaths,
    TimeProvider timeProvider,
    IOptions<DownloadManagerOptions> options,
    DownloadTelemetry telemetry,
    ILogger<HttpDownloader> logger)
{
    /// <summary>
    /// Runs one queued download through the HTTP pipeline and records a terminal
    /// result when the transfer completes, fails, or is canceled.
    /// </summary>
    public async Task ProcessDownloadAsync(QueuedDownload download, CancellationToken globalCancellation)
    {
        var downloadId = download.Id;
        var downloadToken = activeDownloads.GetToken(downloadId);

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["DownloadId"] = downloadId,
            ["OriginalHost"] = download.Domain,
            ["DestinationPath"] = download.DestinationPath,
            ["SourceType"] = download.SourceType,
            ["SourceId"] = download.SourceId
        });
        using var activity = telemetry.StartDownloadActivity(download);
        var attempt = new DownloadAttempt(download.Domain, timeProvider.GetTimestamp());

        // Create a combined token for this specific download
        using var overallTimeout = DownloadTimeoutScope.Start(timeProvider, options.Value.Timeouts.OverallTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            globalCancellation,
            downloadToken,
            overallTimeout.Token);
        var combinedToken = linkedCts.Token;

        try
        {
            await ProcessCore(download, downloadId, attempt, activity, overallTimeout, combinedToken);
        }
        catch (DownloadTimeoutException ex)
        {
            RecordFailure(download, attempt, DownloadFailureCategory.Timeout, DownloadTerminalOutcome.Failed, ex);
            await lifecycle.MarkFailedAsync(
                downloadId,
                ex.Message,
                DownloadFailureCategory.Timeout);
        }
        catch (TimeoutRejectedException ex)
        {
            var message = $"Download timed out while waiting for the response headers after {options.Value.Timeouts.ResponseHeaderTimeout}.";
            RecordFailure(download, attempt, DownloadFailureCategory.Timeout, DownloadTerminalOutcome.Failed, ex);
            await lifecycle.MarkFailedAsync(
                downloadId,
                message,
                DownloadFailureCategory.Timeout);
        }
        catch (OperationCanceledException)
            when (downloadToken.IsCancellationRequested && !globalCancellation.IsCancellationRequested)
        {
            var duration = attempt.GetElapsed(timeProvider);
            telemetry.RecordDownloadCanceled(download, duration);
            SetActivityTerminalTags(activity, attempt, DownloadTerminalOutcome.Canceled);
            logger.LogInformation(
                "Download canceled after {DurationMs} ms with {Bytes} bytes transferred",
                duration.TotalMilliseconds,
                attempt.BytesTransferred);
            await lifecycle.MarkCanceledAsync(downloadId);
        }
        catch (OperationCanceledException ex)
            when (overallTimeout.TimedOut && !globalCancellation.IsCancellationRequested)
        {
            var timeoutException = DownloadTimeoutException.Overall(options.Value.Timeouts.OverallTimeout, ex);
            RecordFailure(download, attempt, DownloadFailureCategory.Timeout, DownloadTerminalOutcome.Failed, timeoutException);
            await lifecycle.MarkFailedAsync(
                downloadId,
                timeoutException.Message,
                DownloadFailureCategory.Timeout);
        }
        catch (OperationCanceledException ex) when (!globalCancellation.IsCancellationRequested)
        {
            var timeoutException = DownloadTimeoutException.Unknown(ex);
            RecordFailure(download, attempt, DownloadFailureCategory.Timeout, DownloadTerminalOutcome.Failed, timeoutException);
            await lifecycle.MarkFailedAsync(
                downloadId,
                timeoutException.Message,
                DownloadFailureCategory.Timeout);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is { } statusCode)
        {
            attempt.HttpStatusCode ??= (int)statusCode;
            RecordFailure(download, attempt, DownloadFailureCategory.Http, DownloadTerminalOutcome.TerminalHttpFailure, ex);
            await lifecycle.MarkFailedAsync(
                downloadId,
                ex.Message,
                DownloadFailureCategory.Http,
                DownloadTerminalOutcome.TerminalHttpFailure,
                (int)statusCode);
        }
        catch (BrokenCircuitException ex)
        {
            RecordFailure(download, attempt, DownloadFailureCategory.Network, DownloadTerminalOutcome.Failed, ex);
            var message = hostCircuitRegistry.TryGetOpenCircuit(download.Domain, out var openUntil)
                ? $"Host circuit for {download.Domain} is open until {openUntil:u}."
                : $"Host circuit for {download.Domain} is open.";

            await lifecycle.MarkFailedAsync(
                downloadId,
                message,
                DownloadFailureCategory.Network);
        }
        catch (MissingDownloadCredentialsException ex)
        {
            RecordFailure(download, attempt, DownloadFailureCategory.Authentication, DownloadTerminalOutcome.ValidationFailed, ex);
            await lifecycle.MarkFailedAsync(
                downloadId,
                ex.Message,
                DownloadFailureCategory.Authentication,
                DownloadTerminalOutcome.ValidationFailed,
                validationMessage: ex.Message);
        }
        catch (DownloadContentTypeException ex)
        {
            RecordFailure(download, attempt, DownloadFailureCategory.Validation, DownloadTerminalOutcome.ValidationFailed, ex);
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
            RecordFailure(download, attempt, DownloadFailureCategory.SizeLimit, DownloadTerminalOutcome.ValidationFailed, ex);
            await lifecycle.MarkFailedAsync(
                downloadId,
                ex.Message,
                DownloadFailureCategory.SizeLimit,
                DownloadTerminalOutcome.ValidationFailed,
                validationMessage: ex.Message);
        }
        catch (InvalidDataException ex)
        {
            RecordFailure(download, attempt, DownloadFailureCategory.Validation, DownloadTerminalOutcome.ValidationFailed, ex);
            await lifecycle.MarkFailedAsync(
                downloadId,
                ex.Message,
                DownloadFailureCategory.Validation,
                DownloadTerminalOutcome.ValidationFailed,
                validationMessage: ex.Message);
        }
        catch (DownloadDiskSpaceException ex)
        {
            RecordFailure(download, attempt, DownloadFailureCategory.Filesystem, DownloadTerminalOutcome.Failed, ex);
            await lifecycle.MarkFailedAsync(
                downloadId,
                ex.Message,
                DownloadFailureCategory.Filesystem);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !globalCancellation.IsCancellationRequested)
        {
            var failureCategory = CategorizeFailure(ex);
            RecordFailure(download, attempt, failureCategory, DownloadTerminalOutcome.Failed, ex, LogLevel.Error);
            await lifecycle.MarkFailedAsync(
                downloadId,
                ex.Message,
                failureCategory);
        }

        void RecordFailure(
            QueuedDownload failedDownload,
            DownloadAttempt failedAttempt,
            DownloadFailureCategory failureCategory,
            DownloadTerminalOutcome outcome,
            Exception exception,
            LogLevel logLevel = LogLevel.Warning)
        {
            var duration = failedAttempt.GetElapsed(timeProvider);
            telemetry.RecordDownloadFailed(
                failedDownload,
                failedAttempt.FinalHost,
                failureCategory,
                outcome,
                failedAttempt.HttpStatusCode,
                duration);
            SetActivityTerminalTags(activity, failedAttempt, outcome, failureCategory);
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            logger.Log(
                logLevel,
                exception,
                "Download failed with category {FailureCategory}, outcome {Outcome}, HTTP status {HttpStatusCode}, final host {FinalHost}, {Bytes} bytes transferred after {DurationMs} ms",
                failureCategory,
                outcome,
                failedAttempt.HttpStatusCode,
                failedAttempt.FinalHost,
                failedAttempt.BytesTransferred,
                duration.TotalMilliseconds);
        }
    }

    private async Task ProcessCore(
        QueuedDownload download,
        Guid downloadId,
        DownloadAttempt attempt,
        Activity? activity,
        DownloadTimeoutScope overallTimeout,
        CancellationToken combinedToken)
    {
        var started = await lifecycle.MarkInProgressAsync(downloadId);
        if (!started)
        {
            logger.LogDebug("Skipping download because it is no longer queued");
            return;
        }

        telemetry.RecordDownloadStarted(download);

        try
        {
            logger.LogInformation(
                "Starting download for host {OriginalHost}, source {SourceType}/{SourceId} -> {DestinationPath}",
                download.Domain,
                download.SourceType,
                download.SourceId,
                download.DestinationPath);

            var stagingPath = stagingPaths.PrepareFreshStagingPath(download);
            logger.LogDebug("Prepared staging path {StagingPath}", stagingPath);

            using var httpClient = httpClientFactory.CreateClient("DownloadClient");
            httpClient.Timeout = Timeout.InfiniteTimeSpan;

            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(download.Url));
            requestHeaderProvider.ApplyHeaders(request);

            using var response = await SendWithHeaderTimeout(httpClient, request, combinedToken);

            attempt.CaptureResponse(response);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "HTTP download response was unsuccessful: {StatusCode} {ReasonPhrase}",
                    (int)response.StatusCode,
                    response.ReasonPhrase);

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
                logger.LogWarning(
                    "Rejected download content type {ContentType}: {ValidationMessage}",
                    contentTypeValidation.ResponseContentType,
                    contentTypeValidation.Message);

                throw new DownloadContentTypeException(
                    contentTypeValidation.Message ?? "Download content type did not match expectations.",
                    contentTypeValidation.ResponseContentType);
            }

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            var maxDownloadSizeBytes = GetMaxDownloadSizeBytes(download);
            logger.LogDebug(
                "HTTP download response accepted with status {StatusCode}, content type {ContentType}, content length {ContentLength}, max bytes {MaxBytes}",
                (int)response.StatusCode,
                response.Content.Headers.ContentType?.ToString(),
                totalBytes,
                maxDownloadSizeBytes);

            if (totalBytes >= 0 && maxDownloadSizeBytes is { } maxBytes && totalBytes > maxBytes)
            {
                throw DownloadSizeLimitException.ForReportedSize(totalBytes, maxBytes);
            }

            if (totalBytes >= 0)
            {
                diskSpaceGuard.EnsureSufficientSpace(download.DestinationPath, totalBytes);
            }

            await stateService.UpdateProgress(downloadId, 0, totalBytes, 0);

            var hashValidators = DownloadHashValidator.Create(download.ExpectedHashes);

            var (bytesDownloaded, startTime) = await StreamToStagingFile(
                download,
                stagingPath,
                response,
                attempt,
                maxDownloadSizeBytes,
                overallTimeout,
                combinedToken);
            if (totalBytes >= 0 && bytesDownloaded != totalBytes)
            {
                throw new InvalidDataException(
                    $"Content-Length mismatch: download ended after {bytesDownloaded} bytes, but the server reported {totalBytes} bytes.");
            }

            hashValidators.Validate(fileSystem, stagingPath);

            stagingPaths.MoveToDestination(download, stagingPath);

            // Final progress update and state change
            var totalElapsed = timeProvider.GetElapsedTime(startTime);

            await stateService.UpdateProgress(downloadId,
                bytesDownloaded,
                totalBytes,
                totalElapsed > TimeSpan.Zero ? bytesDownloaded / totalElapsed.TotalSeconds : 0);

            await lifecycle.MarkCompletedAsync(downloadId, new()
            {
                Outcome = DownloadTerminalOutcome.Completed,
                HttpStatusCode = (int)response.StatusCode,
                ResponseContentType = response.Content.Headers.ContentType?.ToString(),
                ResponseETag = response.Headers.ETag?.ToString(),
                ResponseLastModified = response.Content.Headers.LastModified
            });

            var processDuration = attempt.GetElapsed(timeProvider);
            telemetry.RecordDownloadCompleted(
                download,
                attempt.FinalHost,
                attempt.HttpStatusCode,
                bytesDownloaded,
                processDuration);
            SetActivityTerminalTags(activity, attempt, DownloadTerminalOutcome.Completed);
            activity?.SetStatus(ActivityStatusCode.Ok);

            logger.LogInformation(
                "Download completed with HTTP status {HttpStatusCode}, final host {FinalHost}, content type {ContentType}, {Bytes} bytes transferred after {DurationMs} ms",
                attempt.HttpStatusCode,
                attempt.FinalHost,
                attempt.ResponseContentType,
                bytesDownloaded,
                processDuration.TotalMilliseconds);
        }
        catch
        {
            DeleteStagingFileBestEffort(downloadId, download.DestinationPath);
            throw;
        }
        finally
        {
            telemetry.RecordDownloadStopped(download);
        }
    }

    private async Task<(long BytesDownloaded, long StartTime)> StreamToStagingFile(
        QueuedDownload download,
        string stagingPath,
        HttpResponseMessage response,
        DownloadAttempt attempt,
        long? maxDownloadSizeBytes,
        DownloadTimeoutScope overallTimeout,
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
        while ((bytesRead = await ReadWithIdleTimeout(contentStream, buffer, overallTimeout, combinedToken)) > 0)
        {
            if (maxDownloadSizeBytes is { } maxBytes && bytesDownloaded > maxBytes - bytesRead)
            {
                throw DownloadSizeLimitException.ForReceivedSize(bytesDownloaded + bytesRead, maxBytes);
            }

            await bandwidthGate.WaitForBytesAsync(download.Domain, bytesRead, combinedToken);
            diskSpaceGuard.EnsureSufficientSpace(
                download.DestinationPath,
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
            attempt.BytesTransferred = bytesDownloaded;

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

    private async Task<HttpResponseMessage> SendWithHeaderTimeout(
        HttpClient httpClient,
        HttpRequestMessage request,
        CancellationToken combinedToken)
    {
        using var headerTimeout = DownloadTimeoutScope.Start(timeProvider, options.Value.Timeouts.ResponseHeaderTimeout);
        using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(combinedToken, headerTimeout.Token);

        try
        {
            return await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                sendCts.Token);
        }
        catch (OperationCanceledException ex) when (headerTimeout.TimedOut && !combinedToken.IsCancellationRequested)
        {
            throw DownloadTimeoutException.ResponseHeaders(options.Value.Timeouts.ResponseHeaderTimeout, ex);
        }
    }

    private async Task<int> ReadWithIdleTimeout(
        Stream contentStream,
        byte[] buffer,
        DownloadTimeoutScope overallTimeout,
        CancellationToken combinedToken)
    {
        using var idleTimeout = DownloadTimeoutScope.Start(timeProvider, options.Value.Timeouts.IdleTimeout);
        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(combinedToken, idleTimeout.Token);

        try
        {
            return await contentStream.ReadAsync(buffer, readCts.Token);
        }
        catch (OperationCanceledException ex) when (idleTimeout.TimedOut && !combinedToken.IsCancellationRequested)
        {
            throw DownloadTimeoutException.Idle(options.Value.Timeouts.IdleTimeout, ex);
        }
        catch (OperationCanceledException ex) when (overallTimeout.TimedOut)
        {
            throw DownloadTimeoutException.Overall(options.Value.Timeouts.OverallTimeout, ex);
        }
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

    private static void SetActivityTerminalTags(
        Activity? activity,
        DownloadAttempt attempt,
        DownloadTerminalOutcome outcome,
        DownloadFailureCategory? failureCategory = null)
    {
        activity?.SetTag("download.final_host", attempt.FinalHost);
        activity?.SetTag("download.outcome", outcome.ToString());
        activity?.SetTag("download.bytes", attempt.BytesTransferred);

        if (attempt.HttpStatusCode is { } statusCode)
        {
            activity?.SetTag("http.response.status_code", statusCode);
        }

        if (failureCategory is { } category)
        {
            activity?.SetTag("download.failure_category", category.ToString());
        }
    }

    private sealed class DownloadAttempt(string originalHost, long startedAt)
    {
        public string OriginalHost { get; } = originalHost;
        public string? FinalHost { get; private set; } = originalHost;
        public int? HttpStatusCode { get; set; }
        public string? ResponseContentType { get; private set; }
        public long BytesTransferred { get; set; }

        public TimeSpan GetElapsed(TimeProvider timeProvider) => timeProvider.GetElapsedTime(startedAt);

        public void CaptureResponse(HttpResponseMessage response)
        {
            FinalHost = response.RequestMessage?.RequestUri?.Host ?? OriginalHost;
            HttpStatusCode = (int)response.StatusCode;
            ResponseContentType = response.Content.Headers.ContentType?.ToString();
        }
    }

    private sealed class DownloadTimeoutScope : IDisposable
    {
        private readonly CancellationTokenSource? _cts;
        private readonly ITimer? _timer;
        private int _timedOut;

        private DownloadTimeoutScope(TimeProvider timeProvider, TimeSpan timeout)
        {
            if (!IsEnabled(timeout))
            {
                return;
            }

            _cts = new();
            _timer = timeProvider.CreateTimer(
                static state => ((DownloadTimeoutScope)state!).CancelForTimeout(),
                this,
                timeout,
                Timeout.InfiniteTimeSpan);
        }

        public CancellationToken Token => _cts?.Token ?? CancellationToken.None;
        public bool TimedOut => Volatile.Read(ref _timedOut) == 1;

        public static DownloadTimeoutScope Start(TimeProvider timeProvider, TimeSpan timeout) => new(timeProvider, timeout);

        public void Dispose()
        {
            _timer?.Dispose();
            _cts?.Dispose();
        }

        private void CancelForTimeout()
        {
            if (Interlocked.Exchange(ref _timedOut, 1) == 1)
            {
                return;
            }

            try
            {
                _cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static bool IsEnabled(TimeSpan timeout)
        {
            return timeout > TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan;
        }
    }

    private sealed class DownloadTimeoutException : TimeoutException
    {
        public DownloadTimeoutException()
        {
        }

        public DownloadTimeoutException(string message) : base(message)
        {
        }

        public DownloadTimeoutException(string message, Exception? innerException) : base(message, innerException)
        {
        }

        public static DownloadTimeoutException ResponseHeaders(TimeSpan timeout, Exception innerException) =>
            new($"Download timed out while waiting for the response headers after {timeout}.", innerException);

        public static DownloadTimeoutException Idle(TimeSpan timeout, Exception innerException) =>
            new($"Download stalled: no bytes were received for {timeout}.", innerException);

        public static DownloadTimeoutException Overall(TimeSpan timeout, Exception innerException) =>
            new($"Download exceeded the overall timeout of {timeout}.", innerException);

        public static DownloadTimeoutException Unknown(Exception innerException) =>
            new("Download timed out before the HTTP transfer completed.", innerException);
    }
}
