using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Octans.Core.Downloads;

namespace Octans.Tests.Downloads;

public class BandwidthLimiterTests
{
    private readonly ILogger<BandwidthLimiter> _logger = Substitute.For<ILogger<BandwidthLimiter>>();
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly BandwidthLimiterOptions _options = new()
    {
        DefaultBytesPerSecond = 1024 * 1024, // 1 MB/s
        TrackingWindow = TimeSpan.FromMinutes(5),
        DomainBytesPerSecond = new()
        {
            ["slowdomain.com"] = 512 * 1024 // 512 KB/s
        }
    };

    [Fact]
    public void IsBandwidthAvailable_EmptyDomain_ReturnsTrue()
    {
        // Arrange
        var limiter = CreateLimiter();

        // Act
        var result = limiter.IsBandwidthAvailable(string.Empty);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsBandwidthAvailable_NewDomain_ReturnsTrue()
    {
        // Arrange
        var limiter = CreateLimiter();

        // Act
        var result = limiter.IsBandwidthAvailable("example.com");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void GetDelayForDomain_EmptyDomain_ReturnsZero()
    {
        // Arrange
        var limiter = CreateLimiter();

        // Act
        var delay = limiter.GetDelayForDomain(string.Empty);

        // Assert
        Assert.Equal(TimeSpan.Zero, delay);
    }

    [Fact]
    public void GetDelayForDomain_NewDomain_ReturnsZero()
    {
        // Arrange
        var limiter = CreateLimiter();

        // Act
        var delay = limiter.GetDelayForDomain("example.com");

        // Assert
        Assert.Equal(TimeSpan.Zero, delay);
    }

    [Fact]
    public void RecordDownload_EmptyDomain_DoesNotThrow()
    {
        // Arrange
        var limiter = CreateLimiter();

        // Act & Assert
        var exception = Record.Exception(() => limiter.RecordDownload(string.Empty, 1000));
        Assert.Null(exception);
    }

    [Fact]
    public void RecordDownload_ZeroBytes_DoesNotThrow()
    {
        // Arrange
        var limiter = CreateLimiter();

        // Act & Assert
        var exception = Record.Exception(() => limiter.RecordDownload("example.com", 0));
        Assert.Null(exception);
    }

    [Fact]
    public void RecordDownload_BelowLimit_BandwidthStaysAvailable()
    {
        // Arrange
        var limiter = CreateLimiter();
        var domain = "example.com";
        var bytesPerSecond = _options.DefaultBytesPerSecond;
        var totalBytesAllowed = bytesPerSecond * _options.TrackingWindow.TotalSeconds;
        var bytesToDownload = totalBytesAllowed / 2; // Half the limit

        // Act
        limiter.RecordDownload(domain, (long)bytesToDownload);

        // Assert
        Assert.True(limiter.IsBandwidthAvailable(domain));
        Assert.Equal(TimeSpan.Zero, limiter.GetDelayForDomain(domain));
    }

    [Fact]
    public void RecordDownload_ExceedsLimit_BandwidthBecomesUnavailable()
    {
        // Arrange
        var limiter = CreateLimiter();
        var domain = "example.com";
        var bytesPerSecond = _options.DefaultBytesPerSecond;
        var totalBytesAllowed = bytesPerSecond * _options.TrackingWindow.TotalSeconds;
        var bytesToDownload = totalBytesAllowed * 2; // Double the limit

        // Act
        limiter.RecordDownload(domain, (long)bytesToDownload);

        // Assert
        Assert.False(limiter.IsBandwidthAvailable(domain));
        Assert.True(limiter.GetDelayForDomain(domain) > TimeSpan.Zero);
    }

    [Fact]
    public void RecordDownload_SlowDomain_UsesCustomLimit()
    {
        // Arrange
        var limiter = CreateLimiter();
        var domain = "slowdomain.com";
        var bytesPerSecond = _options.DomainBytesPerSecond[domain];
        var totalBytesAllowed = bytesPerSecond * _options.TrackingWindow.TotalSeconds;
        
        // Act - Download just over the limit
        limiter.RecordDownload(domain, (long)totalBytesAllowed + 1);

        // Assert
        Assert.False(limiter.IsBandwidthAvailable(domain));
    }

    [Fact]
    public void BandwidthAvailability_AfterTimeAdvances_BecomesAvailableAgain()
    {
        // Arrange
        var limiter = CreateLimiter();
        var domain = "example.com";
        var bytesPerSecond = _options.DefaultBytesPerSecond;
        var totalBytesAllowed = bytesPerSecond * _options.TrackingWindow.TotalSeconds;
        var bytesToDownload = totalBytesAllowed * 2; // Double the limit

        // Act 1 - Exceed the limit
        limiter.RecordDownload(domain, (long)bytesToDownload);
        
        // Assert 1 - Bandwidth should be unavailable
        Assert.False(limiter.IsBandwidthAvailable(domain));
        var delay = limiter.GetDelayForDomain(domain);
        Assert.True(delay > TimeSpan.Zero);
        
        // Act 2 - Advance time past the delay
        _timeProvider.Advance(delay + TimeSpan.FromSeconds(1));
        
        // Assert 2 - Bandwidth should be available again
        Assert.True(limiter.IsBandwidthAvailable(domain));
        Assert.Equal(TimeSpan.Zero, limiter.GetDelayForDomain(domain));
    }

    [Fact]
    public void CleanupTimer_RemovesOldRecords()
    {
        // Arrange
        var limiter = CreateLimiter();
        var domain = "example.com";
        var bytesPerSecond = _options.DefaultBytesPerSecond;
        var totalBytesAllowed = bytesPerSecond * _options.TrackingWindow.TotalSeconds;
        
        // Act 1 - Download just under the limit
        limiter.RecordDownload(domain, (long)totalBytesAllowed - 1);
        
        // Assert 1 - Bandwidth should be available
        Assert.True(limiter.IsBandwidthAvailable(domain));
        
        // Act 2 - Advance time past the tracking window
        _timeProvider.Advance(_options.TrackingWindow + TimeSpan.FromMinutes(1));
        
        // This should trigger the cleanup timer
        // We need to manually trigger it for testing since we're using a fake timer
        // In real usage, the timer would fire automatically
        typeof(BandwidthLimiter)
            .GetMethod("CleanupOldRecords", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(limiter, new object?[] { null });
        
        // Act 3 - Download just under the limit again
        limiter.RecordDownload(domain, (long)totalBytesAllowed - 1);
        
        // Assert 3 - Bandwidth should still be available because old records were cleaned up
        Assert.True(limiter.IsBandwidthAvailable(domain));
    }

    [Fact]
    public void MultipleDownloads_AccumulateCorrectly()
    {
        // Arrange
        var limiter = CreateLimiter();
        var domain = "example.com";
        var bytesPerSecond = _options.DefaultBytesPerSecond;
        var totalBytesAllowed = bytesPerSecond * _options.TrackingWindow.TotalSeconds;
        var bytesPerDownload = totalBytesAllowed / 4; // Quarter of the limit per download
        
        // Act 1 - First download
        limiter.RecordDownload(domain, (long)bytesPerDownload);
        
        // Assert 1
        Assert.True(limiter.IsBandwidthAvailable(domain));
        
        // Act 2 - Second download
        limiter.RecordDownload(domain, (long)bytesPerDownload);
        
        // Assert 2
        Assert.True(limiter.IsBandwidthAvailable(domain));
        
        // Act 3 - Third download
        limiter.RecordDownload(domain, (long)bytesPerDownload);
        
        // Assert 3
        Assert.True(limiter.IsBandwidthAvailable(domain));
        
        // Act 4 - Fourth download (now at limit)
        limiter.RecordDownload(domain, (long)bytesPerDownload);
        
        // Assert 4
        Assert.True(limiter.IsBandwidthAvailable(domain));
        
        // Act 5 - Fifth download (exceeds limit)
        limiter.RecordDownload(domain, (long)bytesPerDownload);
        
        // Assert 5
        Assert.False(limiter.IsBandwidthAvailable(domain));
    }

    private BandwidthLimiter CreateLimiter()
    {
        var optionsWrapper = Options.Create(_options);
        return new(_logger, optionsWrapper, _timeProvider);
    }
}
