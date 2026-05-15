using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Octans.Core.Downloads.Bandwidth;

/// <summary>
/// Registers byte-aware bandwidth limiting for the download manager.
/// </summary>
public static class BandwidthLimiterExtensions
{
    /// <summary>
    /// Replaces the download manager's no-op bandwidth gate with a token-bucket gate.
    /// </summary>
    public static IServiceCollection AddBandwidthLimiter(
        this IServiceCollection services,
        Action<BandwidthLimiterOptions>? configure = null)
    {
        services.Configure<BandwidthLimiterOptions>(options => configure?.Invoke(options));
        services.RemoveAll<IDownloadBandwidthGate>();
        services.AddSingleton<IDownloadBandwidthGate, DownloadBandwidthGate>();

        return services;
    }
}
