using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Octans.Core.Downloads.Bandwidth;
using Octans.Core.Downloads.Models;

namespace Octans.Core.Downloads;

public static class DownloadServiceExtensions
{
    public static IServiceCollection AddDownloadManager(
        this IServiceCollection services,
        Action<DownloadManagerOptions>? configure = null)
    {
        // Add options
        var options = new DownloadManagerOptions();
        configure?.Invoke(options);
        services.RemoveAll<DownloadManagerOptions>();
        services.AddSingleton(options);

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
}
