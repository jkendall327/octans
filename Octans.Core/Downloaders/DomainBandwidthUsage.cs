namespace Octans.Core.Downloaders;

public class DomainBandwidthUsage
{
    public long BytesPerHour { get; set; }
    private long _bytesUsed;
    private DateTimeOffset _lastResetTime;
    private readonly TimeProvider _timeProvider;

    public DomainBandwidthUsage(long bytesPerHour, TimeProvider? timeProvider = null)
    {
        BytesPerHour = bytesPerHour;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _lastResetTime = _timeProvider.GetUtcNow();
    }

    public (bool CanMakeRequest, TimeSpan RetryAfter) CanMakeRequest(long requestSize)
    {
        ResetIfNecessary();

        if (_bytesUsed + requestSize <= BytesPerHour)
        {
            return (true, TimeSpan.Zero);
        }

        var nextResetTime = _lastResetTime.AddHours(1);
        var retryAfter = nextResetTime - _timeProvider.GetUtcNow();
        return (false, retryAfter);
    }

    public void AddUsage(long bytes)
    {
        ResetIfNecessary();
        Interlocked.Add(ref _bytesUsed, bytes);
    }

    private void ResetIfNecessary()
    {
        var now = _timeProvider.GetUtcNow();

        if (!((now - _lastResetTime).TotalHours >= 1)) return;

        _bytesUsed = 0;
        _lastResetTime = now;
    }
}