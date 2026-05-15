using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Octans.Core.Downloads.Bandwidth;
using Octans.Tests.Helpers;

namespace Octans.Tests.Downloads;

public sealed class DownloadBandwidthGateTests
{
    private readonly FakeTimeProvider _timeProvider = new(TestClock.UtcNow);

    [Fact]
    public async Task WaitForBytesAsync_AllowsOneSecondInitialDomainBurst()
    {
        var sut = CreateGate(new()
        {
            DefaultBytesPerSecond = 100
        });

        await sut.WaitForBytesAsync("example.com", 100, CancellationToken.None);
    }

    [Fact]
    public async Task WaitForBytesAsync_WaitsForDomainDeficit()
    {
        var sut = CreateGate(new()
        {
            DefaultBytesPerSecond = 100
        });

        var waitTask = sut.WaitForBytesAsync("example.com", 200, CancellationToken.None);

        Assert.False(waitTask.IsCompleted);

        _timeProvider.Advance(TimeSpan.FromMilliseconds(999));
        Assert.False(waitTask.IsCompleted);

        _timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        await waitTask.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task WaitForBytesAsync_UsesDomainOverride()
    {
        var sut = CreateGate(new()
        {
            DefaultBytesPerSecond = 1000,
            DomainBytesPerSecond = new()
            {
                ["slow.example"] = 50
            }
        });

        var waitTask = sut.WaitForBytesAsync("slow.example", 100, CancellationToken.None);

        Assert.False(waitTask.IsCompleted);

        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        await waitTask.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task WaitForBytesAsync_UsesGlobalLimitWhenItIsSlower()
    {
        var sut = CreateGate(new()
        {
            DefaultBytesPerSecond = 1000,
            GlobalBytesPerSecond = 100
        });

        var waitTask = sut.WaitForBytesAsync("example.com", 200, CancellationToken.None);

        Assert.False(waitTask.IsCompleted);

        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        await waitTask.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task WaitForBytesAsync_CanBeDisabledWithZeroRates()
    {
        var sut = CreateGate(new()
        {
            DefaultBytesPerSecond = 0,
            GlobalBytesPerSecond = 0
        });

        await sut.WaitForBytesAsync("example.com", long.MaxValue, CancellationToken.None);
    }

    private DownloadBandwidthGate CreateGate(BandwidthLimiterOptions options)
    {
        return new(Options.Create(options), _timeProvider);
    }
}
