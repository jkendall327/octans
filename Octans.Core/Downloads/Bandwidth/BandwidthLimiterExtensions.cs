using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Octans.Core.Downloads.Bandwidth;

public static class BandwidthLimiterExtensions
{
    public static IServiceCollection AddBandwidthLimiter(
        this IServiceCollection services,
        Action<BandwidthLimiterOptions>? configure = null)
    {
        services.Configure<BandwidthLimiterOptions>(options => configure?.Invoke(options));
        services.RemoveAll<IBandwidthLimiter>();
        services.RemoveAll<IDownloadBandwidthGate>();
        services.AddSingleton<IBandwidthLimiter, BandwidthLimiter>();
        services.AddSingleton<IDownloadBandwidthGate, DownloadBandwidthGate>();

        return services;
    }
}
