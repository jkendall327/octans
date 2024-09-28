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

    public bool CanMakeRequest(long requestSize)
    {
        ResetIfNecessary();
        return _bytesUsed + requestSize <= BytesPerHour;
    }

    public void AddUsage(long bytes)
    {
        ResetIfNecessary();
        Interlocked.Add(ref _bytesUsed, bytes);
    }

    private void ResetIfNecessary()
    {
        var now = _timeProvider.GetUtcNow();
        if ((now - _lastResetTime).TotalHours >= 1)
        {
            _bytesUsed = 0;
            _lastResetTime = now;
        }
    }
}