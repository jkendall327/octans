using Microsoft.Extensions.Options;

namespace Octans.Core.Http.Bandwidth;

/// <summary>
/// Reserves bandwidth budget for streamed download chunks before they are written.
/// </summary>
public interface IDownloadBandwidthGate
{
    Task WaitForBytesAsync(string domain, long byteCount, CancellationToken cancellationToken);
}

/// <summary>
/// Bandwidth gate used when byte pacing is not configured.
/// </summary>
internal sealed class NoOpDownloadBandwidthGate : IDownloadBandwidthGate
{
    public Task WaitForBytesAsync(string domain, long byteCount, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// Token-bucket bandwidth gate that applies both global and per-domain limits.
/// </summary>
internal sealed class DownloadBandwidthGate(
    IOptions<BandwidthLimiterOptions> options,
    TimeProvider timeProvider) : IDownloadBandwidthGate
{
    private readonly BandwidthLimiterOptions _options = options.Value;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, BucketState> _domainBuckets = new(StringComparer.OrdinalIgnoreCase);
    private readonly BucketState _globalBucket = new();

    public async Task WaitForBytesAsync(string domain, long byteCount, CancellationToken cancellationToken)
    {
        if (byteCount <= 0)
        {
            return;
        }

        var delay = ReserveDelay(domain, byteCount);
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        await Task.Delay(delay, timeProvider, cancellationToken);
    }

    private TimeSpan ReserveDelay(string domain, long byteCount)
    {
        lock (_lock)
        {
            var now = timeProvider.GetUtcNow();
            var globalDelay = Reserve(_globalBucket, _options.GlobalBytesPerSecond, byteCount, now);
            var domainDelay = Reserve(GetDomainBucket(domain), GetDomainBytesPerSecond(domain), byteCount, now);

            return globalDelay >= domainDelay ? globalDelay : domainDelay;
        }
    }

    private BucketState GetDomainBucket(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return BucketState.Unlimited;
        }

        if (_domainBuckets.TryGetValue(domain, out var bucket))
        {
            return bucket;
        }

        bucket = new();
        _domainBuckets[domain] = bucket;
        return bucket;
    }

    private long GetDomainBytesPerSecond(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return 0;
        }

        return _options.DomainBytesPerSecond.GetValueOrDefault(domain, _options.DefaultBytesPerSecond);
    }

    private static TimeSpan Reserve(BucketState bucket, long bytesPerSecond, long byteCount, DateTimeOffset now)
    {
        if (bytesPerSecond <= 0 || bucket.IsUnlimited)
        {
            return TimeSpan.Zero;
        }

        var capacity = (double)bytesPerSecond;
        var effectiveNow = now;

        if (bucket.LastRefill is null)
        {
            bucket.LastRefill = effectiveNow;
            bucket.Tokens = capacity;
        }
        else if (bucket.LastRefill.Value <= effectiveNow)
        {
            var elapsedSeconds = (effectiveNow - bucket.LastRefill.Value).TotalSeconds;
            bucket.Tokens = Math.Min(capacity, bucket.Tokens + elapsedSeconds * bytesPerSecond);
            bucket.LastRefill = effectiveNow;
        }
        else
        {
            effectiveNow = bucket.LastRefill.Value;
        }

        if (bucket.Tokens >= byteCount)
        {
            bucket.Tokens -= byteCount;
            return TimeSpan.Zero;
        }

        var missingBytes = byteCount - bucket.Tokens;
        var delay = TimeSpan.FromSeconds(missingBytes / bytesPerSecond);

        bucket.Tokens = 0;
        bucket.LastRefill = effectiveNow + delay;

        return bucket.LastRefill.Value - now;
    }

    private sealed class BucketState
    {
        public static BucketState Unlimited { get; } = new() { IsUnlimited = true };

        public bool IsUnlimited { get; init; }
        public DateTimeOffset? LastRefill { get; set; }
        public double Tokens { get; set; }
    }
}
