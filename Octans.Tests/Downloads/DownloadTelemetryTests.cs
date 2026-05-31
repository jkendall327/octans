using System.Diagnostics.Metrics;
using Octans.Core.Http;
using Octans.Data.Models;

namespace Octans.Tests.Downloads;

public sealed class DownloadTelemetryTests
{
    [Fact]
    public void RecordDownloadCompleted_EmitsHostScopedOutcomeMetrics()
    {
        var measurements = new List<MetricMeasurement<long>>();
        using var listener = CreateListener(measurements);
        listener.Start();

        using var telemetry = new DownloadTelemetry();
        var download = CreateDownload();

        telemetry.RecordDownloadCompleted(
            download,
            finalHost: "cdn.example.com",
            httpStatusCode: 200,
            bytes: 1234,
            duration: TimeSpan.FromSeconds(2));

        var snapshot = measurements.ToList();
        Assert.Contains(snapshot, measurement =>
            measurement.Name == "octans.downloads.completed" &&
            measurement.Value == 1 &&
            measurement.Tags["download.original_host"] as string == "example.com" &&
            measurement.Tags["download.final_host"] as string == "cdn.example.com" &&
            measurement.Tags["download.source_type"] as string == "RawUrl" &&
            Equals(measurement.Tags["http.response.status_code"], 200));
        Assert.Contains(snapshot, measurement =>
            measurement.Name == "octans.downloads.bytes" &&
            measurement.Value == 1234 &&
            measurement.Tags["download.original_host"] as string == "example.com");
    }

    [Fact]
    public void SetQueueDepth_EmitsGlobalAndHostScopedGaugeMeasurements()
    {
        var measurements = new List<MetricMeasurement<int>>();
        using var listener = CreateListener(measurements);
        listener.Start();

        using var telemetry = new DownloadTelemetry();

        telemetry.SetQueueDepth(new Dictionary<string, int>
        {
            ["example.com"] = 2,
            ["cdn.example.com"] = 3
        });
        listener.RecordObservableInstruments();

        var snapshot = measurements.ToList();
        Assert.Contains(snapshot, measurement =>
            measurement.Name == "octans.downloads.queue.depth" &&
            measurement.Value == 5 &&
            !measurement.Tags.ContainsKey("download.original_host"));
        Assert.Contains(snapshot, measurement =>
            measurement.Name == "octans.downloads.queue.depth" &&
            measurement.Value == 2 &&
            measurement.Tags["download.original_host"] as string == "example.com");
    }

    private static MeterListener CreateListener<T>(List<MetricMeasurement<T>> measurements)
        where T : struct
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == DownloadTelemetry.MeterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };

        listener.SetMeasurementEventCallback<T>((instrument, value, tags, _) =>
        {
            measurements.Add(new(
                instrument.Name,
                value,
                tags.ToArray().ToDictionary(
                    tag => tag.Key,
                    tag => tag.Value,
                    StringComparer.Ordinal)));
        });

        return listener;
    }

    private static QueuedDownload CreateDownload()
    {
        return new()
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file.jpg",
            DestinationPath = "/downloads/file.jpg",
            Domain = "example.com",
            SourceType = "RawUrl",
            SourceId = "import-1"
        };
    }

    private sealed record MetricMeasurement<T>(
        string Name,
        T Value,
        IReadOnlyDictionary<string, object?> Tags);
}
