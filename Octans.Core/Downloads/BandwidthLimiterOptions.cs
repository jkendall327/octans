namespace Octans.Core.Downloads;

public class BandwidthLimiterOptions
{
    public Dictionary<string, long> DomainBytesPerSecond { get; init; } = new();
    public long DefaultBytesPerSecond { get; set; } = 1024 * 1024; // 1 MB/s default
    public TimeSpan TrackingWindow { get; init; } = TimeSpan.FromMinutes(5);
}