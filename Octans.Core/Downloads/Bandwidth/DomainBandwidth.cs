using System.Collections.Concurrent;

namespace Octans.Core.Downloads.Bandwidth;

/// <summary>
/// Legacy rolling-window bandwidth state for a single domain.
/// </summary>
public class DomainBandwidth
{
    public required string Domain { get; set; }
    public long BandwidthLimit { get; set; } // in bytes per second

    public ConcurrentQueue<(DateTimeOffset Timestamp, long Usage)> UsageHistory { get; init; } = new();

    public TimeSpan WindowDuration { get; set; } = TimeSpan.FromSeconds(60); // Default 60-second window
}

/// <summary>
/// Legacy bandwidth configuration grouped by domain.
/// </summary>
public class DomainBandwidthOptions
{
    public List<IndividualDomainBandwidthOptions> Domains { get; init; } = [];
}

/// <summary>
/// Legacy bandwidth limit for one domain.
/// </summary>
public class IndividualDomainBandwidthOptions
{
    public required string Domain { get; init; }
    public long BandwidthLimit { get; init; } // in bytes per second
    public int WindowDurationSeconds { get; init; } = 60; // Default 60-second window
}
