using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Octans.Core.Downloads.Bandwidth;
using Octans.Core.Downloads.Models;
using Polly;

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
        services.TryAddSingleton<IInFlightDownloadCoordinator, InFlightDownloadCoordinator>();
        services.TryAddSingleton<IDownloadCompletionNotifier, NoOpDownloadCompletionNotifier>();
        services.TryAddSingleton<IDownloadJobResultService, DownloadJobResultService>();
        services.TryAddSingleton<IDownloadBandwidthGate, NoOpDownloadBandwidthGate>();
        services.TryAddSingleton<IDownloadDiskSpaceGuard, DownloadDiskSpaceGuard>();
        services.TryAddSingleton<IDownloadHostCircuitRegistry, DownloadHostCircuitRegistry>();
        services.TryAddSingleton<IDownloadRequestHeaderProvider, DownloadRequestHeaderProvider>();
        services.TryAddSingleton<DownloadStagingPaths>();
        services.TryAddSingleton<IDownloadLifecycleService, DownloadLifecycleService>();
        services.TryAddSingleton<HttpDownloader>();
        services.TryAddSingleton<IDownloadService, DownloadService>();
        services.TryAddSingleton<IDownloadQueue, DatabaseDownloadQueue>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DownloadBackgroundService>());

        services.AddHttpClient("DownloadClient")
            .AddStandardResilienceHandler()
            .Configure((resilienceOptions, provider) =>
                ConfigureDownloadResilience(resilienceOptions, provider))
            .SelectPipelineBy(_ => request => request.RequestUri?.Host ?? "unknown-host");

        return services;
    }

    private static void ConfigureDownloadResilience(
        HttpStandardResilienceOptions resilienceOptions,
        IServiceProvider provider)
    {
        var downloadOptions = provider.GetRequiredService<IOptions<DownloadManagerOptions>>().Value;
        var breakerOptions = downloadOptions.HostCircuitBreaker;

        resilienceOptions.Retry.MaxRetryAttempts = breakerOptions.MaxRetryAttempts;
        resilienceOptions.Retry.Delay = breakerOptions.RetryDelay;
        resilienceOptions.Retry.BackoffType = DelayBackoffType.Exponential;
        resilienceOptions.Retry.UseJitter = true;

        var circuitRegistry = provider.GetRequiredService<IDownloadHostCircuitRegistry>();
        resilienceOptions.CircuitBreaker.FailureRatio = breakerOptions.FailureRatio;
        resilienceOptions.CircuitBreaker.MinimumThroughput = breakerOptions.MinimumThroughput;
        resilienceOptions.CircuitBreaker.SamplingDuration = breakerOptions.SamplingDuration;
        resilienceOptions.CircuitBreaker.BreakDuration = breakerOptions.BreakDuration;
        resilienceOptions.CircuitBreaker.OnOpened = args =>
        {
            var domain = args.Context.GetRequestMessage()?.RequestUri?.Host;
            if (domain is not null)
            {
                circuitRegistry.OpenCircuit(domain, args.BreakDuration);
            }

            return default;
        };
        resilienceOptions.CircuitBreaker.OnClosed = args =>
        {
            var domain = args.Context.GetRequestMessage()?.RequestUri?.Host;
            if (domain is not null)
            {
                circuitRegistry.CloseCircuit(domain);
            }

            return default;
        };
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
