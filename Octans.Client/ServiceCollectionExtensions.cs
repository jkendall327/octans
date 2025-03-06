using System.IO.Abstractions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Octans.Client.Components.Pages;
using Octans.Client.HealthChecks;
using Octans.Core;
using Octans.Core.Communication;
using Octans.Core.Infrastructure;
using Refit;

namespace Octans.Client;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOctansServices(this IServiceCollection services)
    {
        services.AddScoped<SubfolderManager>();
        services.AddSingleton<ImportRequestSender>();
        services.AddHostedService<ImportFolderBackgroundService>();
        services.AddTransient<OctansApiHealthCheck>();

        return services;
    }

    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IFileSystem>(new FileSystem());
        services.AddSingleton<StorageService>();
        services.AddHealthChecks()
            .AddCheck<OctansApiHealthCheck>("octans-api");

        return services;
    }

    public static IServiceCollection AddHttpClients(this IServiceCollection services)
    {
        services.AddRefitClient<IOctansApi>().ConfigureHttpClient(client =>
        {
            var port = CommunicationConstants.OCTANS_SERVER_PORT;
            client.BaseAddress = new($"http://localhost:{port}/");
        });

        return services;
    }

    public static IServiceCollection AddViewmodels(this IServiceCollection services)
    {
        services.AddScoped<GalleryViewmodel>();
        services.AddScoped<ImportsViewmodel>();
        return services;
    }

    public static void SetupConfiguration(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration.GetSection(nameof(GlobalSettings));
        builder.Services.Configure<GlobalSettings>(config);
    }

    public static IEndpointRouteBuilder MapStaticAssets(this IEndpointRouteBuilder app)
    {
        var serviceProvider = app.ServiceProvider;
        var globalSettings = serviceProvider.GetRequiredService<IOptions<GlobalSettings>>().Value;
        
        if (!string.IsNullOrEmpty(globalSettings.AppRoot) && Directory.Exists(globalSettings.AppRoot))
        {
            app.MapGet("/approot/{**path}", async (string path, HttpContext context) =>
            {
                var fullPath = Path.Combine(globalSettings.AppRoot, path);
                if (File.Exists(fullPath))
                {
                    var contentType = GetContentType(Path.GetExtension(fullPath));
                    context.Response.ContentType = contentType;
                    await context.Response.SendFileAsync(fullPath);
                    return;
                }
                context.Response.StatusCode = 404;
            });

            // Also serve static files directly
            var fileProvider = new PhysicalFileProvider(globalSettings.AppRoot);
            var staticFileOptions = new StaticFileOptions
            {
                FileProvider = fileProvider,
                RequestPath = "/approot"
            };
            
            ((IApplicationBuilder)app).UseStaticFiles(staticFileOptions);
        }
        
        return app;
    }

    private static string GetContentType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".html" or ".htm" => "text/html",
            ".txt" => "text/plain",
            ".json" => "application/json",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            _ => "application/octet-stream"
        };
    }
}
