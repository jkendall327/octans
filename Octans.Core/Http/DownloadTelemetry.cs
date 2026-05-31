using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Octans.Data.Models;

namespace Octans.Core.Http;

/// <summary>
/// Emits Octans-owned download telemetry. HTTP request spans and low-level HTTP
/// metrics come from the standard HttpClient instrumentation.
/// </summary>
public sealed class DownloadTelemetry : IDisposable
{
    public const string ActivitySourceName = "Octans.Core.Http.Downloads";
    public const string MeterName = "Octans.Core.Http.Downloads";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _downloadsStarted;
    private readonly Counter<long> _downloadsCompleted;
    private readonly Counter<long> _downloadsFailed;
    private readonly Counter<long> _downloadsCanceled;
    private readonly Counter<long> _bytesTransferred;
    private readonly Counter<long> _retries;
    private readonly Counter<long> _circuitTransitions;
    private readonly Histogram<double> _downloadDuration;
    private readonly ObservableGauge<int> _queueDepth;
    private readonly ObservableGauge<int> _activeDownloads;
    private readonly object _queueDepthLock = new();
    private readonly object _activeDownloadsLock = new();
    private Dictionary<string, int> _queueDepthByDomain = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, int> _activeDownloadsByDomain = new(StringComparer.OrdinalIgnoreCase);

    public DownloadTelemetry()
    {
        _downloadsStarted = _meter.CreateCounter<long>(
            "octans.downloads.started",
            description: "Number of downloads that Octans started processing.");
        _downloadsCompleted = _meter.CreateCounter<long>(
            "octans.downloads.completed",
            description: "Number of downloads that completed successfully.");
        _downloadsFailed = _meter.CreateCounter<long>(
            "octans.downloads.failed",
            description: "Number of downloads that reached a failed terminal state.");
        _downloadsCanceled = _meter.CreateCounter<long>(
            "octans.downloads.canceled",
            description: "Number of downloads canceled while Octans was processing them.");
        _bytesTransferred = _meter.CreateCounter<long>(
            "octans.downloads.bytes",
            unit: "By",
            description: "Payload bytes transferred by completed downloads.");
        _retries = _meter.CreateCounter<long>(
            "octans.downloads.retries",
            description: "Retry attempts made by the download HTTP resilience pipeline.");
        _circuitTransitions = _meter.CreateCounter<long>(
            "octans.downloads.circuit.transitions",
            description: "Host circuit breaker open and close transitions.");
        _downloadDuration = _meter.CreateHistogram<double>(
            "octans.downloads.duration",
            unit: "s",
            description: "Elapsed time spent processing downloads.");
        _queueDepth = _meter.CreateObservableGauge(
            "octans.downloads.queue.depth",
            ObserveQueueDepth,
            description: "Queued downloads waiting for the background worker.");
        _activeDownloads = _meter.CreateObservableGauge(
            "octans.downloads.active",
            ObserveActiveDownloads,
            description: "Downloads currently being processed.");
    }

    public Activity? StartDownloadActivity(QueuedDownload download)
    {
        var activity = ActivitySource.StartActivity("octans.download.process", ActivityKind.Internal);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag("download.id", download.Id);
        activity.SetTag("download.original_host", download.Domain);
        activity.SetTag("download.source_type", EmptyIfNull(download.SourceType));
        activity.SetTag("download.source_id", EmptyIfNull(download.SourceId));
        activity.SetTag("download.destination_path", download.DestinationPath);

        return activity;
    }

    public void RecordDownloadStarted(QueuedDownload download)
    {
        _downloadsStarted.Add(1, BuildHostTags(download.Domain, download.SourceType));

        lock (_activeDownloadsLock)
        {
            IncrementDomain(_activeDownloadsByDomain, download.Domain);
        }
    }

    public void RecordDownloadCompleted(
        QueuedDownload download,
        string? finalHost,
        int? httpStatusCode,
        long bytes,
        TimeSpan duration)
    {
        var tags = BuildTerminalTags(
            download.Domain,
            finalHost,
            download.SourceType,
            DownloadTerminalOutcome.Completed,
            failureCategory: null,
            httpStatusCode);

        _downloadsCompleted.Add(1, tags);
        _bytesTransferred.Add(bytes, BuildHostTags(download.Domain, download.SourceType));
        _downloadDuration.Record(duration.TotalSeconds, tags);
    }

    public void RecordDownloadFailed(
        QueuedDownload download,
        string? finalHost,
        DownloadFailureCategory failureCategory,
        DownloadTerminalOutcome outcome,
        int? httpStatusCode,
        TimeSpan duration)
    {
        var tags = BuildTerminalTags(
            download.Domain,
            finalHost,
            download.SourceType,
            outcome,
            failureCategory,
            httpStatusCode);

        _downloadsFailed.Add(1, tags);
        _downloadDuration.Record(duration.TotalSeconds, tags);
    }

    public void RecordDownloadCanceled(QueuedDownload download, TimeSpan duration)
    {
        var tags = BuildTerminalTags(
            download.Domain,
            finalHost: null,
            download.SourceType,
            DownloadTerminalOutcome.Canceled,
            failureCategory: null,
            httpStatusCode: null);

        _downloadsCanceled.Add(1, tags);
        _downloadDuration.Record(duration.TotalSeconds, tags);
    }

    public void RecordDownloadStopped(QueuedDownload download)
    {
        lock (_activeDownloadsLock)
        {
            DecrementDomain(_activeDownloadsByDomain, download.Domain);
        }
    }

    public void RecordRetry(string? domain, int attemptNumber, TimeSpan retryDelay)
    {
        _retries.Add(1,
            new("download.original_host", NormalizeDomain(domain)),
            new("retry.attempt", attemptNumber),
            new("retry.delay_ms", retryDelay.TotalMilliseconds));
    }

    public void RecordCircuitOpened(string domain, TimeSpan breakDuration)
    {
        _circuitTransitions.Add(1,
            new("download.original_host", NormalizeDomain(domain)),
            new("circuit.state", "open"),
            new("circuit.break_duration_ms", breakDuration.TotalMilliseconds));
    }

    public void RecordCircuitClosed(string domain)
    {
        _circuitTransitions.Add(1,
            new("download.original_host", NormalizeDomain(domain)),
            new("circuit.state", "closed"));
    }

    public void SetQueueDepth(IReadOnlyDictionary<string, int> queueDepthByDomain)
    {
        lock (_queueDepthLock)
        {
            _queueDepthByDomain = new(queueDepthByDomain, StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Dispose()
    {
        _meter.Dispose();
    }

    private IEnumerable<Measurement<int>> ObserveQueueDepth()
    {
        lock (_queueDepthLock)
        {
            return BuildGaugeMeasurements(_queueDepthByDomain);
        }
    }

    private IEnumerable<Measurement<int>> ObserveActiveDownloads()
    {
        lock (_activeDownloadsLock)
        {
            return BuildGaugeMeasurements(_activeDownloadsByDomain);
        }
    }

    private static ReadOnlyCollection<Measurement<int>> BuildGaugeMeasurements(Dictionary<string, int> valuesByDomain)
    {
        var measurements = new List<Measurement<int>>
        {
            new(valuesByDomain.Values.Sum())
        };

        foreach (var (domain, value) in valuesByDomain)
        {
            measurements.Add(new(value, new KeyValuePair<string, object?>("download.original_host", domain)));
        }

        return measurements.AsReadOnly();
    }

    private static TagList BuildHostTags(string? originalHost, string? sourceType)
    {
        var tags = new TagList
        {
            { "download.original_host", NormalizeDomain(originalHost) },
            { "download.source_type", EmptyIfNull(sourceType) }
        };

        return tags;
    }

    private static TagList BuildTerminalTags(
        string? originalHost,
        string? finalHost,
        string? sourceType,
        DownloadTerminalOutcome outcome,
        DownloadFailureCategory? failureCategory,
        int? httpStatusCode)
    {
        var tags = BuildHostTags(originalHost, sourceType);
        tags.Add("download.final_host", NormalizeDomain(finalHost));
        tags.Add("download.outcome", outcome.ToString());

        if (failureCategory is { } category)
        {
            tags.Add("download.failure_category", category.ToString());
        }

        if (httpStatusCode is { } statusCode)
        {
            tags.Add("http.response.status_code", statusCode);
        }

        return tags;
    }

    private static void IncrementDomain(Dictionary<string, int> valuesByDomain, string? domain)
    {
        var normalizedDomain = NormalizeDomain(domain);
        valuesByDomain.TryGetValue(normalizedDomain, out var count);
        valuesByDomain[normalizedDomain] = count + 1;
    }

    private static void DecrementDomain(Dictionary<string, int> valuesByDomain, string? domain)
    {
        var normalizedDomain = NormalizeDomain(domain);
        if (!valuesByDomain.TryGetValue(normalizedDomain, out var count))
        {
            return;
        }

        if (count <= 1)
        {
            valuesByDomain.Remove(normalizedDomain);
            return;
        }

        valuesByDomain[normalizedDomain] = count - 1;
    }

    private static string NormalizeDomain(string? domain)
    {
        return string.IsNullOrWhiteSpace(domain)
            ? "unknown"
            : domain.Trim().ToLowerInvariant();
    }

    private static string EmptyIfNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value;
    }
}
