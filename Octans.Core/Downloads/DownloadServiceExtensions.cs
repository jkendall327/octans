using Microsoft.Extensions.DependencyInjection;

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
        services.AddSingleton(options);
        
        services.AddSingleton<DownloadStateService>();
        services.AddScoped<DownloadProcessor>();
        services.AddScoped<IDownloadService, DownloadService>();
        services.AddSingleton<IDownloadQueue, DatabaseDownloadQueue>();
        
        // Add HTTP client
        services.AddHttpClient("DownloadClient", client => 
        {
            // Configure default headers, etc.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MyApp/1.0");
        });
        
        return services;
    }
}