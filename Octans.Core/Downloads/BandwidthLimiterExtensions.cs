using Microsoft.Extensions.DependencyInjection;

namespace Octans.Core.Downloads;

public static class BandwidthLimiterExtensions
{
    public static IServiceCollection AddBandwidthLimiter(
        this IServiceCollection services,
        Action<BandwidthLimiterOptions>? configure = null)
    {
        services.Configure<BandwidthLimiterOptions>(options => configure?.Invoke(options));
        services.AddSingleton<IBandwidthLimiter, BandwidthLimiter>();

        return services;
    }
}