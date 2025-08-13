using System.IO.Abstractions;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Octans.Client.Components.Pages;
using Octans.Client.Options;
using Octans.Core;
using Octans.Core.Communication;
using Octans.Core.Downloaders;
using Octans.Core.Importing;
using Octans.Core.Infrastructure;
using Octans.Core.Models;
using Octans.Core.Querying;
using Octans.Core.Tags;
using Octans.Server;
using Octans.Server.Services;
using Refit;

namespace Octans.Client;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOctansServices(this IServiceCollection services)
    {
        services.AddHostedService<ImportFolderBackgroundService>();
        services.AddHostedService<SubscriptionBackgroundService>();

        services.AddScoped<SubfolderManager>();
        services.AddSingleton<StorageService>();
        services.AddScoped<StatsService>();

        return services;
    }

    public static void AddInfrastructure(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IFileSystem>(new FileSystem());
        builder.Services.AddSingleton(TimeProvider.System);
    }

    public static void AddChannels(this WebApplicationBuilder builder)
    {
        var channel = Channel.CreateBounded<ThumbnailCreationRequest>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

        builder.Services.AddSingleton(channel.Reader);
        builder.Services.AddSingleton(channel.Writer);
    }

    public static void AddBusinessServices(this WebApplicationBuilder builder)
    {
        // Imports
        builder.Services.AddScoped<SimpleImporter>();
        builder.Services.AddScoped<FileImporter>();
        builder.Services.AddScoped<PostImporter>();
        builder.Services.AddScoped<IImporter, Importer>();

        // Import services
        builder.Services.AddScoped<ReimportChecker>();
        builder.Services.AddScoped<DatabaseWriter>();
        builder.Services.AddScoped<FilesystemWriter>();
        builder.Services.AddScoped<ImportFilterService>();
        builder.Services.AddSingleton<ThumbnailCreator>();
        builder.Services.AddScoped<DownloaderFactory>();
        builder.Services.AddScoped<DownloaderService>();

        // Files
        builder.Services.AddSingleton<SubfolderManager>();
        builder.Services.AddScoped<FileFinder>();
        builder.Services.AddScoped<FileDeleter>();
        builder.Services.AddScoped<TagUpdater>();

        // Queries
        builder.Services.AddScoped<IQueryService, QueryService>();
        builder.Services.AddScoped<QueryParser>();
        builder.Services.AddScoped<QueryPlanner>();
        builder.Services.AddScoped<QueryTagConverter>();
        builder.Services.AddScoped<HashSearcher>();

        // Stats
        builder.Services.AddScoped<StatsService>();
        builder.Services.AddScoped<StorageService>();

        builder.Services.AddMemoryCache();
    }

    public static void AddDatabase(this WebApplicationBuilder builder)
    {
        builder.Services.AddDbContextFactory<ServerDbContext>(BuildDatabase);
        builder.Services.AddDbContext<ServerDbContext>(BuildDatabase, optionsLifetime: ServiceLifetime.Singleton);

        builder.Services
            .AddHealthChecks()
            .AddDbContextCheck<ServerDbContext>("database", HealthStatus.Unhealthy);
    }

    private static void BuildDatabase(IServiceProvider s, DbContextOptionsBuilder opt)
    {
        var config = s.GetRequiredService<IOptions<GlobalSettings>>();

        var root = config.Value.AppRoot;

        var path = s.GetRequiredService<IFileSystem>().Path;

        var dbFolder = path.Join(root, "db");

        var db = path.Join(dbFolder, "octans.db");

        opt.UseSqlite($"Data Source={db};");
    }

    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IFileSystem>(new FileSystem());

        return services;
    }

    public static IServiceCollection AddHttpClients(this IServiceCollection services)
    {
        services.AddRefitClient<IOctansApi>().ConfigureHttpClient(client =>
        {
            var port = CommunicationConstants.OctansServerPort;
            client.BaseAddress = new($"http://localhost:{port}/");
        });

        return services;
    }

    public static IServiceCollection AddViewmodels(this IServiceCollection services)
    {
        // Imports
        services.AddScoped<IRawUrlImportViewmodel, RawUrlImportViewmodel>();
        services.AddScoped<ILocalFileImportViewmodel, LocalFileImportViewmodel>();

        services.AddScoped<GalleryViewmodel>();
        services.AddScoped<Config.ConfigViewModel>();

        return services;
    }

    public static void SetupConfiguration(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<GlobalSettings>(
            builder.Configuration.GetSection(nameof(GlobalSettings)));

        builder.Services.Configure<SubscriptionOptions>(
            builder.Configuration.GetSection(SubscriptionOptions.SectionName));

        builder.Services.AddOptions<ThumbnailOptions>()
            .BindConfiguration(ThumbnailOptions.ConfigurationSectionName)
            .ValidateDataAnnotations();

        builder.Services.AddOptions<ImportFolderOptions>()
            .BindConfiguration(ImportFolderOptions.ConfigurationSectionName)
            .ValidateDataAnnotations();

        var configuration = builder.Configuration.GetSection("GlobalSettings");
        builder.Services.Configure<GlobalSettings>(configuration);
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
