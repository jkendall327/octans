using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Octans.Core.Http.Bandwidth;
using Octans.Core.Http.Models;
using Polly;

namespace Octans.Core.Http;

/// <summary>
/// Dependency-injection registration helpers for the download manager.
/// </summary>
public static class DownloadServiceExtensions
{
    /// <summary>
    /// Registers the durable download queue, background worker, HTTP downloader,
    /// state tracker, and default no-op extension points.
    /// </summary>
    public static IServiceCollection AddDownloadManager(
        this IServiceCollection services,
        Action<DownloadManagerOptions>? configure = null)
    {
        services.RemoveAll<DownloadManagerOptions>();
        services.AddOptions<DownloadManagerOptions>()
            .Configure(options => configure?.Invoke(options));

        services.TryAddSingleton<IDownloadStateService, DownloadStatusTracker>();
        services.TryAddSingleton<IActiveDownloadRegistry, ActiveDownloadRegistry>();
        services.TryAddSingleton<IDownloadCompletionNotifier, NoOpDownloadCompletionNotifier>();
        services.TryAddSingleton<IDownloadJobResultService, DownloadJobResultService>();
        services.TryAddSingleton<IDownloadCompletionWaiter, DownloadCompletionWaiter>();
        services.TryAddSingleton<IDownloadBandwidthGate, NoOpDownloadBandwidthGate>();
        services.TryAddSingleton<IDownloadDiskSpaceGuard, DownloadDiskSpaceGuard>();
        services.TryAddSingleton<IDownloadHostCircuitRegistry, DownloadHostCircuitRegistry>();
        services.TryAddSingleton<IDownloadRequestHeaderProvider, DownloadRequestHeaderProvider>();
        services.TryAddSingleton<DownloadTelemetry>();
        services.TryAddSingleton<DownloadStagingPaths>();
        services.TryAddSingleton<IDownloadLifecycleService, DownloadLifecycleService>();
        services.TryAddSingleton<HttpDownloader>();
        services.TryAddSingleton<IDownloadService, DownloadService>();
        services.TryAddSingleton<IDownloadQueue, DatabaseDownloadQueue>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DownloadBackgroundService>());

        services.AddHttpClient("DownloadClient", client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(provider =>
            {
                var downloadOptions = provider.GetRequiredService<IOptions<DownloadManagerOptions>>().Value;

                return new SocketsHttpHandler
                {
                    ConnectTimeout = GetEnabledTimeout(downloadOptions.Timeouts.ConnectionTimeout) ??
                                     Timeout.InfiniteTimeSpan
                };
            })
            .AddStandardResilienceHandler()
            .Configure(ConfigureDownloadResilience)
            .SelectPipelineBy(_ => request => request.RequestUri?.Host ?? "unknown-host");

        return services;
    }

    private static void ConfigureDownloadResilience(
        HttpStandardResilienceOptions resilienceOptions,
        IServiceProvider provider)
    {
        var downloadOptions = provider.GetRequiredService<IOptions<DownloadManagerOptions>>().Value;
        var breakerOptions = downloadOptions.HostCircuitBreaker;
        var timeoutOptions = downloadOptions.Timeouts;

        resilienceOptions.Retry.MaxRetryAttempts = breakerOptions.MaxRetryAttempts;
        resilienceOptions.Retry.Delay = breakerOptions.RetryDelay;
        resilienceOptions.Retry.BackoffType = DelayBackoffType.Exponential;
        resilienceOptions.Retry.UseJitter = true;
        resilienceOptions.TotalRequestTimeout.Timeout = timeoutOptions.OverallTimeout;
        resilienceOptions.AttemptTimeout.Timeout = timeoutOptions.ResponseHeaderTimeout;

        var circuitRegistry = provider.GetRequiredService<IDownloadHostCircuitRegistry>();
        var telemetry = provider.GetRequiredService<DownloadTelemetry>();

        resilienceOptions.Retry.OnRetry = args =>
        {
            var domain = args.Context.GetRequestMessage()?.RequestUri?.Host;
            telemetry.RecordRetry(domain, args.AttemptNumber, args.RetryDelay);

            return default;
        };

        resilienceOptions.CircuitBreaker.FailureRatio = breakerOptions.FailureRatio;
        resilienceOptions.CircuitBreaker.MinimumThroughput = breakerOptions.MinimumThroughput;
        resilienceOptions.CircuitBreaker.SamplingDuration = Max(
            breakerOptions.SamplingDuration,
            Double(timeoutOptions.ResponseHeaderTimeout));
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

    private static TimeSpan? GetEnabledTimeout(TimeSpan timeout)
    {
        return timeout > TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan
            ? timeout
            : null;
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right)
    {
        return left >= right ? left : right;
    }

    private static TimeSpan Double(TimeSpan value)
    {
        return value >= TimeSpan.FromTicks(TimeSpan.MaxValue.Ticks / 2)
            ? TimeSpan.MaxValue
            : TimeSpan.FromTicks(value.Ticks * 2);
    }

    /// <summary>
    /// Registers the download manager using configuration-bound options.
    /// </summary>
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
