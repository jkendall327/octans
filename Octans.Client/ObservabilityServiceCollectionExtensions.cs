using Octans.Core.Http;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Octans.Client;

public static class ObservabilityServiceCollectionExtensions
{
    public static IServiceCollection AddOctansObservability(this WebApplicationBuilder builder)
    {
        var serviceVersion = typeof(Program).Assembly.GetName().Version?.ToString();
        var useOtlpExporter = builder.Configuration.GetValue<bool>("OpenTelemetry:OtlpExporter:Enabled")
                              || !string.IsNullOrWhiteSpace(
                                  builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: builder.Environment.ApplicationName,
                serviceVersion: serviceVersion))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(DownloadTelemetry.ActivitySourceName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                if (useOtlpExporter)
                {
                    tracing.AddOtlpExporter();
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(DownloadTelemetry.MeterName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                if (useOtlpExporter)
                {
                    metrics.AddOtlpExporter();
                }
            });

        return builder.Services;
    }
}
