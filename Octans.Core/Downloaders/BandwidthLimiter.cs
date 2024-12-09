using System.Collections.Concurrent;

namespace Octans.Core.Downloaders;

public class BandwidthLimiter
{
    private readonly ConcurrentDictionary<string, DomainBandwidthUsage> _domainUsage = new();
    private readonly TimeProvider _timeProvider;

    public BandwidthLimiter(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public void SetBandwidthLimit(string domain, long bytesPerHour)
    {
        _domainUsage.AddOrUpdate(domain,
            _ => new DomainBandwidthUsage(bytesPerHour, _timeProvider),
            (_, existing) =>
            {
                existing.BytesPerHour = bytesPerHour;
                return existing;
            });
    }

    public void TrackUsage(string domain, long bytes)
    {
        if (_domainUsage.TryGetValue(domain, out var usage))
        {
            usage.AddUsage(bytes);
        }
    }

    public void EnsureCanMakeRequest(string domain, long requestSize)
    {
        if (!_domainUsage.TryGetValue(domain, out var usage)) return;

        (var canMakeRequest, var retryAfter) = usage.CanMakeRequest(requestSize);

        if (!canMakeRequest)
        {
            // TODO: reintroduce a domain-specific exception here.
            throw new InvalidOperationException("Too many requests");
        }
    }
}