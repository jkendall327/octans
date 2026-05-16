namespace Octans.Core.Http.Bandwidth;

/// <summary>
/// Byte-per-second limits used by the download bandwidth gate.
/// </summary>
public class BandwidthLimiterOptions
{
    public Dictionary<string, long> DomainBytesPerSecond { get; init; } = new();
    public long GlobalBytesPerSecond { get; set; }
    public long DefaultBytesPerSecond { get; set; } = 1024 * 1024; // 1 MB/s default
}
