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

    private readonly BandwidthLimiter _sut;

    public BandwidthLimiterTests()
    {
        var optionsWrapper = Options.Create(_options);
        _sut = new(_logger, optionsWrapper, _timeProvider);
    }

    [Fact]
    public void IsBandwidthAvailable_EmptyDomain_ReturnsTrue()
    {
        // Act
        var result = _sut.IsBandwidthAvailable(string.Empty);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsBandwidthAvailable_NewDomain_ReturnsTrue()
    {
        // Act
        var result = _sut.IsBandwidthAvailable("example.com");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void GetDelayForDomain_EmptyDomain_ReturnsZero()
    {
        // Act
        var delay = _sut.GetDelayForDomain(string.Empty);

        // Assert
        Assert.Equal(TimeSpan.Zero, delay);
    }

    [Fact]
    public void GetDelayForDomain_NewDomain_ReturnsZero()
    {
        // Act
        var delay = _sut.GetDelayForDomain("example.com");

        // Assert
        Assert.Equal(TimeSpan.Zero, delay);
    }

    [Fact]
    public void RecordDownload_EmptyDomain_DoesNotThrow()
    {
        // Act & Assert
        var exception = Record.Exception(() => _sut.RecordDownload(string.Empty, 1000));
        Assert.Null(exception);
    }

    [Fact]
    public void RecordDownload_ZeroBytes_DoesNotThrow()
    {
        // Act & Assert
        var exception = Record.Exception(() => _sut.RecordDownload("example.com", 0));
        Assert.Null(exception);
    }

    [Fact]
    public void RecordDownload_BelowLimit_BandwidthStaysAvailable()
    {
        // Arrange
        var domain = "example.com";
        var bytesPerSecond = _options.DefaultBytesPerSecond;
        var totalBytesAllowed = bytesPerSecond * _options.TrackingWindow.TotalSeconds;
        var bytesToDownload = totalBytesAllowed / 2; // Half the limit

        // Act
        _sut.RecordDownload(domain, (long)bytesToDownload);

        // Assert
        Assert.True(_sut.IsBandwidthAvailable(domain));
        Assert.Equal(TimeSpan.Zero, _sut.GetDelayForDomain(domain));
    }

    [Fact]
    public void RecordDownload_ExceedsLimit_BandwidthBecomesUnavailable()
    {
        // Arrange
        var domain = "example.com";
        var bytesPerSecond = _options.DefaultBytesPerSecond;
        var totalBytesAllowed = bytesPerSecond * _options.TrackingWindow.TotalSeconds;
        var bytesToDownload = totalBytesAllowed * 2; // Double the limit

        // Act
        _sut.RecordDownload(domain, (long)bytesToDownload);

        // Assert
        Assert.False(_sut.IsBandwidthAvailable(domain));
        Assert.True(_sut.GetDelayForDomain(domain) > TimeSpan.Zero);
    }

    [Fact]
    public void RecordDownload_SlowDomain_UsesCustomLimit()
    {
        // Arrange
        var domain = "slowdomain.com";
        var bytesPerSecond = _options.DomainBytesPerSecond[domain];
        var totalBytesAllowed = bytesPerSecond * _options.TrackingWindow.TotalSeconds;

        // Act - Download just over the limit
        _sut.RecordDownload(domain, (long)totalBytesAllowed + 1);

        // Assert
        Assert.False(_sut.IsBandwidthAvailable(domain));
    }

    [Fact]
    public void BandwidthAvailability_AfterTimeAdvances_BecomesAvailableAgain()
    {
        // Arrange
        var domain = "example.com";
        var bytesPerSecond = _options.DefaultBytesPerSecond;
        var totalBytesAllowed = bytesPerSecond * _options.TrackingWindow.TotalSeconds;
        var bytesToDownload = totalBytesAllowed * 2; // Double the limit

        // Act 1 - Exceed the limit
        _sut.RecordDownload(domain, (long)bytesToDownload);

        // Assert 1 - Bandwidth should be unavailable
        Assert.False(_sut.IsBandwidthAvailable(domain));
        var delay = _sut.GetDelayForDomain(domain);
        Assert.True(delay > TimeSpan.Zero);

        // Act 2 - Advance time past the delay
        _timeProvider.Advance(delay + TimeSpan.FromSeconds(1));

        // Assert 2 - Bandwidth should be available again
        Assert.True(_sut.IsBandwidthAvailable(domain));
        Assert.Equal(TimeSpan.Zero, _sut.GetDelayForDomain(domain));
    }

    [Fact]
    public void CleanupTimer_RemovesOldRecords()
    {
        // Arrange
        var domain = "example.com";
        var bytesPerSecond = _options.DefaultBytesPerSecond;
        var totalBytesAllowed = bytesPerSecond * _options.TrackingWindow.TotalSeconds;

        // Act 1 - Download just under the limit
        _sut.RecordDownload(domain, (long)totalBytesAllowed - 1);

        // Assert 1 - Bandwidth should be available
        Assert.True(_sut.IsBandwidthAvailable(domain));

        // Act 2 - Advance time past the tracking window
        _timeProvider.Advance(_options.TrackingWindow + TimeSpan.FromMinutes(1));
        
        // Trigger the timer by advancing time and firing all pending timers
        _timeProvider.FireAllTimers();

        // Act 3 - Download just under the limit again
        _sut.RecordDownload(domain, (long)totalBytesAllowed - 1);

        // Assert 3 - Bandwidth should still be available because old records were cleaned up
        Assert.True(_sut.IsBandwidthAvailable(domain));
    }

    [Fact]
    public void MultipleDownloads_AccumulateCorrectly()
    {
        // Arrange
        var domain = "example.com";
        var bytesPerSecond = _options.DefaultBytesPerSecond;
        var totalBytesAllowed = bytesPerSecond * _options.TrackingWindow.TotalSeconds;
        var bytesPerDownload = totalBytesAllowed / 4; // Quarter of the limit per download

        // Act 1 - First download
        _sut.RecordDownload(domain, (long)bytesPerDownload);

        // Assert 1
        Assert.True(_sut.IsBandwidthAvailable(domain));

        // Act 2 - Second download
        _sut.RecordDownload(domain, (long)bytesPerDownload);

        // Assert 2
        Assert.True(_sut.IsBandwidthAvailable(domain));

        // Act 3 - Third download
        _sut.RecordDownload(domain, (long)bytesPerDownload);

        // Assert 3
        Assert.True(_sut.IsBandwidthAvailable(domain));

        // Act 4 - Fourth download (now at limit)
        _sut.RecordDownload(domain, (long)bytesPerDownload);

        // Assert 4
        Assert.True(_sut.IsBandwidthAvailable(domain));

        // Act 5 - Fifth download (exceeds limit)
        _sut.RecordDownload(domain, (long)bytesPerDownload);

        // Assert 5
        Assert.False(_sut.IsBandwidthAvailable(domain));
    }

}
