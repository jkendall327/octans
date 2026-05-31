using Microsoft.Extensions.Logging;

namespace Octans.Core.Http;

/// <summary>
/// Keeps track of hosts whose HTTP resilience circuit is temporarily open.
/// </summary>
public interface IDownloadHostCircuitRegistry
{
    IReadOnlySet<string> GetOpenDomains();
    bool TryGetOpenCircuit(string domain, out DateTimeOffset openUntil);
    void OpenCircuit(string domain, TimeSpan breakDuration);
    void CloseCircuit(string domain);
}

/// <summary>
/// In-memory registry used by the HTTP resilience pipeline and queue scheduler
/// to avoid dispatching work to temporarily unavailable hosts.
/// </summary>
public sealed class DownloadHostCircuitRegistry(
    TimeProvider timeProvider,
    ILogger<DownloadHostCircuitRegistry> logger,
    DownloadTelemetry telemetry) : IDownloadHostCircuitRegistry
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, DateTimeOffset> _openUntilByDomain = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> GetOpenDomains()
    {
        lock (_lock)
        {
            PruneExpiredOpenCircuits();

            return _openUntilByDomain.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }

    public bool TryGetOpenCircuit(string domain, out DateTimeOffset openUntil)
    {
        lock (_lock)
        {
            if (!_openUntilByDomain.TryGetValue(domain, out openUntil))
            {
                return false;
            }

            if (openUntil > timeProvider.GetUtcNow())
            {
                return true;
            }

            _openUntilByDomain.Remove(domain);
            return false;
        }
    }

    public void OpenCircuit(string domain, TimeSpan breakDuration)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return;
        }

        var normalizedDomain = NormalizeDomain(domain);
        var openUntil = timeProvider.GetUtcNow().Add(breakDuration);

        lock (_lock)
        {
            _openUntilByDomain[normalizedDomain] = openUntil;
        }

        telemetry.RecordCircuitOpened(normalizedDomain, breakDuration);
        logger.LogWarning("Opened download host circuit for {Domain} until {OpenUntil}", normalizedDomain, openUntil);
    }

    public void CloseCircuit(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return;
        }

        var normalizedDomain = NormalizeDomain(domain);

        lock (_lock)
        {
            _openUntilByDomain.Remove(normalizedDomain);
        }

        telemetry.RecordCircuitClosed(normalizedDomain);
        logger.LogInformation("Closed download host circuit for {Domain}", normalizedDomain);
    }

    private void PruneExpiredOpenCircuits()
    {
        var now = timeProvider.GetUtcNow();
        var expiredDomains = _openUntilByDomain
            .Where(kvp => kvp.Value <= now)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var domain in expiredDomains)
        {
            _openUntilByDomain.Remove(domain);
        }
    }

    private static string NormalizeDomain(string domain) => domain.Trim().ToLowerInvariant();
}
