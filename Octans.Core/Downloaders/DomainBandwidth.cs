using System.Collections.Concurrent;

namespace Octans.Core.Downloaders;

public class DomainBandwidth
{
    public required string Domain { get; set; }
    public long BandwidthLimit { get; set; } // in bytes per second

    public ConcurrentQueue<(DateTime Timestamp, long Usage)> UsageHistory { get; set; } = new();

    public TimeSpan WindowDuration { get; set; } = TimeSpan.FromSeconds(60); // Default 60-second window
}

public class DomainBandwidthOptions
{
    public List<IndividualDomainBandwidthOptions> Domains { get; set; } = new();
}

public class IndividualDomainBandwidthOptions
{
    public required string Domain { get; set; }
    public long BandwidthLimit { get; set; } // in bytes per second
    public int WindowDurationSeconds { get; set; } = 60; // Default 60-second window
}
