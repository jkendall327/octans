using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Octans.Core.Downloaders;

public class BandwidthLimiter
{
    private readonly ConcurrentDictionary<string, DomainBandwidth> _domainBandwidths = new();
    private readonly IOptions<DomainBandwidthOptions> _options;

    public BandwidthLimiter(IOptions<DomainBandwidthOptions> options)
    {
        _options = options;
        InitializeBandwidthSettings();
    }

    private void InitializeBandwidthSettings()
    {
        if (_options.Value.Domains == null || !_options.Value.Domains.Any())
            return;

        foreach (var domainOption in _options.Value.Domains)
        {
            _domainBandwidths.TryAdd(domainOption.Domain, new DomainBandwidth
            {
                Domain = domainOption.Domain,
                BandwidthLimit = domainOption.BandwidthLimit,
                WindowDuration = TimeSpan.FromSeconds(domainOption.WindowDurationSeconds)
            });
        }
    }

    public bool CanMakeRequest(string domain, long requestSize)
    {
        var bandwidth = _domainBandwidths.GetOrAdd(domain, new DomainBandwidth { Domain = domain, BandwidthLimit = 1000000 });

        var now = DateTime.UtcNow;
        var windowStart = now - bandwidth.WindowDuration;

        // Remove outdated entries
        while (bandwidth.UsageHistory.TryPeek(out var oldest) && oldest.Timestamp < windowStart)
        {
            bandwidth.UsageHistory.TryDequeue(out _);
        }

        // Calculate current usage within the window
        var currentUsage = bandwidth.UsageHistory.Sum(entry => entry.Usage);

        // Check if the request would exceed the limit
        if (currentUsage + requestSize > bandwidth.BandwidthLimit * bandwidth.WindowDuration.TotalSeconds)
            return false;

        // Add new usage entry
        bandwidth.UsageHistory.Enqueue((now, requestSize));
        return true;
    }

    public void SetBandwidthLimit(string domain, long limit, TimeSpan? windowDuration = null)
    {
        _domainBandwidths.AddOrUpdate(domain,
            new DomainBandwidth { Domain = domain, BandwidthLimit = limit, WindowDuration = windowDuration ?? TimeSpan.FromSeconds(60) },
            (key, existing) =>
            {
                existing.BandwidthLimit = limit;
                if (windowDuration.HasValue) existing.WindowDuration = windowDuration.Value;
                return existing;
            });
    }
}
