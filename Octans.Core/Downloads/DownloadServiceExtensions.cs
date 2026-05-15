using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Octans.Core.Downloads.Bandwidth;
using Octans.Core.Downloads.Models;

namespace Octans.Core.Downloads;

public static class DownloadServiceExtensions
{
    public static IServiceCollection AddDownloadManager(
        this IServiceCollection services,
        Action<DownloadManagerOptions>? configure = null)
    {
        services.RemoveAll<DownloadManagerOptions>();
        services.AddOptions<DownloadManagerOptions>()
            .Configure(options => configure?.Invoke(options));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<DownloadManagerOptions>>().Value);

        services.TryAddSingleton<IDownloadStateService, DownloadStatusTracker>();
        services.TryAddSingleton<IActiveDownloadRegistry, ActiveDownloadRegistry>();
        services.TryAddSingleton<IDownloadCompletionNotifier, NoOpDownloadCompletionNotifier>();
        services.TryAddSingleton<IDownloadJobResultService, DownloadJobResultService>();
        services.TryAddSingleton<IDownloadBandwidthGate, NoOpDownloadBandwidthGate>();
        services.TryAddSingleton<DownloadStagingPaths>();
        services.TryAddSingleton<IDownloadLifecycleService, DownloadLifecycleService>();
        services.TryAddSingleton<HttpDownloader>();
        services.TryAddSingleton<IDownloadService, DownloadService>();
        services.TryAddSingleton<IDownloadQueue, DatabaseDownloadQueue>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DownloadBackgroundService>());

        // Add HTTP client
        services.AddHttpClient("DownloadClient", client =>
        {
            // Configure default headers, etc.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Octans/1.0");
        });

        return services;
    }

    public static IServiceCollection AddDownloadManager(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<DownloadManagerOptions>? configure = null)
    {
        return services.AddDownloadManager(options =>
        {
            configuration.Bind(options);
            configure?.Invoke(options);
        });
    }
}
