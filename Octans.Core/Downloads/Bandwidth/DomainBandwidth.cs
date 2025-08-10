using System.Collections.Concurrent;

namespace Octans.Core.Downloads;

public class DomainBandwidth
{
    public required string Domain { get; set; }
    public long BandwidthLimit { get; set; } // in bytes per second

    public ConcurrentQueue<(DateTime Timestamp, long Usage)> UsageHistory { get; init; } = new();

    public TimeSpan WindowDuration { get; set; } = TimeSpan.FromSeconds(60); // Default 60-second window
}

public class DomainBandwidthOptions
{
    public List<IndividualDomainBandwidthOptions> Domains { get; init; } = [];
}

public class IndividualDomainBandwidthOptions
{
    public required string Domain { get; init; }
    public long BandwidthLimit { get; init; } // in bytes per second
    public int WindowDurationSeconds { get; init; } = 60; // Default 60-second window
}
