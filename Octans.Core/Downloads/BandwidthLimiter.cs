using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Octans.Core.Downloads;

public class BandwidthLimiterOptions
{
    public Dictionary<string, long> DomainBytesPerSecond { get; init; } = new();
    public long DefaultBytesPerSecond { get; init; } = 1024 * 1024; // 1 MB/s default
    public TimeSpan TrackingWindow { get; init; } = TimeSpan.FromMinutes(5);
}

public interface IBandwidthLimiterService
{
    bool IsBandwidthAvailable(string domain);
    TimeSpan GetDelayForDomain(string domain);
    void RecordDownload(string domain, long bytes);
}

public sealed class BandwidthLimiterService : IBandwidthLimiterService, IDisposable
{
    private readonly ILogger<BandwidthLimiterService> _logger;
    private readonly BandwidthLimiterOptions _options;
    
    // Track downloads per domain with timestamps
    private readonly ConcurrentDictionary<string, Queue<(DateTime Timestamp, long Bytes)>> _domainUsage = new();
    
    // Track when a domain can next be used
    private readonly ConcurrentDictionary<string, DateTime> _domainNextAvailable = new();
    
    // Timer to clean up old records
    private readonly Timer _cleanupTimer;

    public BandwidthLimiterService(
        ILogger<BandwidthLimiterService> logger,
        IOptions<BandwidthLimiterOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        
        // Set up cleanup timer to run every minute
        _cleanupTimer = new(CleanupOldRecords, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public bool IsBandwidthAvailable(string domain)
    {
        if (string.IsNullOrEmpty(domain))
        {
            return true;
        }

        // Check if domain is in cooldown
        if (_domainNextAvailable.TryGetValue(domain, out var nextAvailable))
        {
            return DateTime.UtcNow >= nextAvailable;
        }
        
        return true;
    }

    public TimeSpan GetDelayForDomain(string domain)
    {
        if (string.IsNullOrEmpty(domain))
        {
            return TimeSpan.Zero;
        }

        if (_domainNextAvailable.TryGetValue(domain, out var nextAvailable))
        {
            var now = DateTime.UtcNow;
            if (nextAvailable > now)
            {
                return nextAvailable - now;
            }
        }
        
        return TimeSpan.Zero;
    }

    public void RecordDownload(string domain, long bytes)
    {
        if (string.IsNullOrEmpty(domain) || bytes <= 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        
        // Add to domain usage records
        _domainUsage.AddOrUpdate(
            domain,
            // If key doesn't exist, create a new queue with this record
            _ => new Queue<(DateTime, long)>(new[] { (now, bytes) }),
            // If key exists, add to the existing queue
            (_, queue) => 
            {
                queue.Enqueue((now, bytes));
                return queue;
            });
            
        // Calculate current bandwidth usage for this domain
        CalculateBandwidthUsage(domain);
    }

    private void CalculateBandwidthUsage(string domain)
    {
        if (!_domainUsage.TryGetValue(domain, out var usageQueue) || usageQueue.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var cutoff = now - _options.TrackingWindow;
        
        // Calculate total bytes in the time window
        long totalBytes = 0;
        var records = usageQueue.ToArray();
        
        foreach ((var timestamp, var bytes) in records)
        {
            if (timestamp >= cutoff)
            {
                totalBytes += bytes;
            }
        }
        
        // Get configured limit for this domain
        var bytesPerSecond = _options.DefaultBytesPerSecond;
        if (_options.DomainBytesPerSecond.TryGetValue(domain, out var domainLimit))
        {
            bytesPerSecond = domainLimit;
        }
        
        // Convert to bytes per window
        var bytesPerWindow = bytesPerSecond * _options.TrackingWindow.TotalSeconds;
        
        // If we've exceeded the limit, calculate when we can download again
        if (!(totalBytes > bytesPerWindow)) return;
        
        // Simple rate limiting: wait until enough of the window has passed
        // that we're back under the limit
        var excessRatio = totalBytes / bytesPerWindow;
        var waitTime = TimeSpan.FromSeconds((excessRatio - 1) * _options.TrackingWindow.TotalSeconds);
            
        // Cap at the tracking window length
        if (waitTime > _options.TrackingWindow)
        {
            waitTime = _options.TrackingWindow;
        }
            
        var nextAvailable = now + waitTime;
            
        _domainNextAvailable.AddOrUpdate(
            domain,
            nextAvailable,
            (_, existing) => nextAvailable > existing ? nextAvailable : existing);
                
        _logger.LogInformation(
            "Domain {Domain} bandwidth limit reached. Next available in {WaitTime}", 
            domain, waitTime);
    }

    private void CleanupOldRecords(object? state)
    {
        try
        {
            var now = DateTime.UtcNow;
            var cutoff = now - _options.TrackingWindow;
            
            // Clean up domain usage records
            foreach (var domain in _domainUsage.Keys)
            {
                if (!_domainUsage.TryGetValue(domain, out var queue)) continue;
                
                // Remove old records
                while (queue.Count > 0 && queue.Peek().Timestamp < cutoff)
                {
                    queue.Dequeue();
                }
                    
                // If queue is empty, consider removing the domain entirely
                if (queue.Count == 0)
                {
                    _domainUsage.TryRemove(domain, out _);
                }
            }
            
            // Clean up expired next available times
            foreach (var domain in _domainNextAvailable.Keys)
            {
                if (_domainNextAvailable.TryGetValue(domain, out var nextAvailable) && nextAvailable <= now)
                {
                    _domainNextAvailable.TryRemove(domain, out _);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during bandwidth limiter cleanup");
        }
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }
}

public static class BandwidthLimiterExtensions
{
    public static IServiceCollection AddBandwidthLimiter(
        this IServiceCollection services,
        Action<BandwidthLimiterOptions>? configure = null)
    {
        services.Configure<BandwidthLimiterOptions>(options => configure?.Invoke(options));
        services.AddSingleton<IBandwidthLimiterService, BandwidthLimiterService>();
        
        return services;
    }
}