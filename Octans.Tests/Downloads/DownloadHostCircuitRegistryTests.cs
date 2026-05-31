using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Octans.Core.Http;
using Octans.Tests.Helpers;

namespace Octans.Tests.Downloads;

public sealed class DownloadHostCircuitRegistryTests
{
    private readonly FakeTimeProvider _timeProvider = new(TestClock.UtcNow);
    private readonly DownloadTelemetry _telemetry = new();
    private readonly DownloadHostCircuitRegistry _registry;

    public DownloadHostCircuitRegistryTests()
    {
        _registry = new(_timeProvider, NullLogger<DownloadHostCircuitRegistry>.Instance, _telemetry);
    }

    [Fact]
    public void GetOpenDomains_ReturnsOnlyUnexpiredDomains()
    {
        _registry.OpenCircuit("Flaky.Example", TimeSpan.FromSeconds(10));
        _registry.OpenCircuit("other.example", TimeSpan.FromSeconds(30));

        _timeProvider.Advance(TimeSpan.FromSeconds(11));

        var openDomains = _registry.GetOpenDomains();

        Assert.DoesNotContain("flaky.example", openDomains);
        Assert.Contains("other.example", openDomains);
    }

    [Fact]
    public void TryGetOpenCircuit_ReturnsFalseAfterBreakDuration()
    {
        _registry.OpenCircuit("flaky.example", TimeSpan.FromSeconds(10));

        Assert.True(_registry.TryGetOpenCircuit("flaky.example", out _));

        _timeProvider.Advance(TimeSpan.FromSeconds(10));

        Assert.False(_registry.TryGetOpenCircuit("flaky.example", out _));
    }
}
