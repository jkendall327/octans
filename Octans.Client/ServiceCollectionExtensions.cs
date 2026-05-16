using System.IO.Abstractions;
using System.Threading.Channels;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Octans.Client.Components.Downloads;
using Octans.Client.Components.Duplicates;
using Octans.Client.Components.Gallery;
using Octans.Client.Components.Importing;
using Octans.Client.Components.MainToolbar;
using Octans.Client.Components.Settings;
using Octans.Client.Components.StatusBar;
using Octans.Client.Components.Subscriptions;
using Octans.Client.Services;
using Octans.Client.Settings;
using Octans.Core;
using Octans.Core.Downloaders;
using Octans.Core.Duplicates;
using Octans.Core.Filesystem;
using Octans.Core.Http;
using Octans.Core.Http.Bandwidth;
using Octans.Core.Importing;
using Octans.Core.Importing.Filters;
using Octans.Core.Importing.ImportFolders;
using Octans.Core.Importing.RawByteProviders;
using Octans.Core.Notes;
using Octans.Core.Progress;
using Octans.Core.Querying;
using Octans.Core.Repositories;
using Octans.Core.Scripting;
using Octans.Core.Stats;
using Octans.Core.Subscriptions;
using Octans.Core.Tags;
using Octans.Core.Thumbnails;
using Octans.Data.Models;

namespace Octans.Client;

public static class ServiceCollectionExtensions
{
    public static void AddKeyProtection(this WebApplicationBuilder builder)
    {
        var fileSystem = new FileSystem();
        var keysFolder = fileSystem.Path.Combine(builder.Environment.ContentRootPath, "keys");

        fileSystem.Directory.CreateDirectory(keysFolder);

        // ASP.NET Data Protection requires a physical DirectoryInfo for persisted keys.
#pragma warning disable RS0030
        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keysFolder));
#pragma warning restore RS0030
    }

    public static IServiceCollection AddOctansServices(this IServiceCollection services)
    {
        services.AddHostedService<ImportFolderBackgroundService>();
        services.AddHostedService<ImportProcessorService>();
        services.AddHostedService<SubscriptionBackgroundService>();
        services.AddHostedService<RepositoryChangeBackgroundService>();

        services.AddSingleton<IBackgroundProgressReporter, BackgroundProgressService>();
        services.AddScoped<ImportFolderScanner>();
        services.AddSingleton<RepositoryChangeProcessor>();

        services.AddSingleton<ImageStorage>();
        services.AddSingleton<StorageService>();
        services.AddScoped<StatsService>();
        services.AddScoped<SubscriptionService>();

        return services;
    }

    public static void AddChannels(this IServiceCollection services)
    {
        var channel = Channel.CreateBounded<ThumbnailCreationRequest>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

        services.AddSingleton(channel.Reader);
        services.AddSingleton(channel.Writer);

        var repoChannel = Channel.CreateBounded<RepositoryChangeRequest>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

        services.AddSingleton(repoChannel.Reader);
        services.AddSingleton(repoChannel.Writer);
    }

    public static void AddBusinessServices(this IServiceCollection services)
    {
        // Imports
        services.AddScoped<SimpleImporter>();
        services.AddScoped<FileImporter>();
        services.AddScoped<PostImporter>();
        services.AddScoped<ImportItemProcessor>();
        services.AddScoped<IImporter, Importer>();
        services.AddScoped<ImportJobService>();
        services.AddScoped<IImportJobService>(s => s.GetRequiredService<ImportJobService>());
        services.AddSingleton<IImportJobNotifier, NoOpImportJobNotifier>();

        // Import services
        services.AddScoped<ReimportChecker>();
        services.AddScoped<DatabaseWriter>();
        services.AddScoped<FilesystemWriter>();
        services.AddScoped<ImportFilterService>();
        services.AddSingleton<ThumbnailCreator>();
        services.AddScoped<DownloaderFactory>();
        services.AddScoped<DownloaderService>();

        // Files
        services.AddSingleton<ImageStorage>();
        services.AddScoped<FileFinder>();
        services.AddScoped<FileDeleter>();
        services.AddScoped<TagUpdater>();
        services.AddScoped<INoteService, NoteService>();

        // Duplicates
        services.AddScoped<IPerceptualHashProvider, PerceptualHashProvider>();
        services.AddScoped<DuplicateService>();

        services.AddSingleton(TimeProvider.System);
        services.AddBandwidthLimiter();
        services.AddDownloadManager();
        services.AddMediator();

        // Queries
        services.AddScoped<IQueryService, QueryService>();
        services.AddScoped<QueryParser>();
        services.AddScoped<QueryPlanner>();
        services.AddScoped<QueryTagConverter>();
        services.AddScoped<HashSearcher>();
        services.AddScoped<QuerySuggestionFinder>();
        services.AddScoped<TagSplitter>();
        services.AddScoped<TagSiblingService>();
        services.AddScoped<TagParentService>();
        services.AddScoped<ITagService, TagService>();

        // Stats
        services.AddScoped<StatsService>();
        services.AddScoped<StorageService>();

        // Scripting
        services.AddScoped<ICustomCommandProvider, CustomCommandProvider>();

        // Subscriptions
        services.AddScoped<ISubscriptionExecutor, NoOpSubscriptionExecutor>();

        services.AddMemoryCache();
    }

    public static void AddDatabase(this IServiceCollection services)
    {
        services.AddDbContextFactory<ServerDbContext>(BuildDatabase);
        services.AddDbContext<ServerDbContext>(BuildDatabase, optionsLifetime: ServiceLifetime.Singleton);

        services
            .AddHealthChecks()
            .AddDbContextCheck<ServerDbContext>("database", HealthStatus.Unhealthy);
    }

    private static void BuildDatabase(IServiceProvider s, DbContextOptionsBuilder opt)
    {
        var config = s.GetRequiredService<IOptions<GlobalSettings>>();

        var root = config.Value.AppRoot;

        var path = s.GetRequiredService<IFileSystem>()
            .Path;

        var dbFolder = path.Join(root, "db");

        var db = path.Join(dbFolder, "octans.db");

        opt.UseSqlite($"Data Source={db};");
    }

    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IFileSystem>(new FileSystem());
        services.AddScoped<IBrowserStorage, BrowserStorage>();
        services.AddScoped<IClipboardService, ClipboardService>();
        services.AddScoped<IBrowserService, BrowserService>();
        services.AddScoped<ThemeService>();
        services.AddScoped<IThemePreferenceService, ThemePreferenceService>();
        services.AddScoped<ShellService>();
        services.AddScoped<ISettingsService, SettingsService>();

        services.AddMediator();

        return services;
    }

    public static IServiceCollection AddViewmodels(this IServiceCollection services)
    {
        // Imports
        services.AddScoped<IRawUrlImportViewmodel, RawUrlImportViewmodel>();
        services.AddScoped<ILocalFileImportViewmodel, LocalFileImportViewmodel>();

        services.AddScoped<GalleryViewmodel>();
        services.AddScoped<ImageViewerViewmodel>();
        services.AddScoped<DetailsPaneViewmodel>();
        services.AddScoped<QueryBuilderViewmodel>();
        services.AddScoped<IKeybindingService, KeybindingService>();
        services.AddScoped<KeybindingManagerViewmodel>();
        services.AddScoped<SettingsViewModel>();
        services.AddScoped<MainToolbarViewmodel>();
        services.AddScoped<StatusBarViewmodel>();
        services.AddScoped<StatusService>();
        services.AddSingleton<ProgressStore>();
        services.AddScoped<DownloadersViewmodel>();
        services.AddScoped<DownloadsViewmodel>();
        services.AddScoped<SubscriptionsViewmodel>();
        services.AddScoped<DuplicateManagerViewmodel>();

        return services;
    }

    public static void SetupConfiguration(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<GlobalSettings>(builder.Configuration.GetSection(nameof(GlobalSettings)));
        builder.Services.Configure<UserSettings>(builder.Configuration.GetSection(nameof(UserSettings)));

        builder
            .Services
            .AddOptions<ThumbnailOptions>()
            .BindConfiguration(ThumbnailOptions.ConfigurationSectionName)
            .ValidateDataAnnotations();

        builder
            .Services
            .AddOptions<ImportFolderOptions>()
            .BindConfiguration(ImportFolderOptions.ConfigurationSectionName)
            .ValidateDataAnnotations();

        var configuration = builder.Configuration.GetSection("GlobalSettings");
        builder.Services.Configure<GlobalSettings>(configuration);
    }

    public static IEndpointRouteBuilder MapStaticAssets(this WebApplication app)
    {
        var serviceProvider = app.Services;

        var globalSettings = serviceProvider.GetRequiredService<IOptions<GlobalSettings>>()
            .Value;

        var fileSystem = serviceProvider.GetRequiredService<IFileSystem>();

        if (string.IsNullOrEmpty(globalSettings.AppRoot) || !fileSystem.Directory.Exists(globalSettings.AppRoot))
        {
            return app;
        }

        app.MapGet("/approot/{**path}",
            async (string path, HttpContext context) =>
            {
                var fullPath = fileSystem.Path.Combine(globalSettings.AppRoot, path);

                if (fileSystem.File.Exists(fullPath))
                {
                    var contentType = GetContentType(fileSystem.Path.GetExtension(fullPath));
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

        app.UseStaticFiles(staticFileOptions);

        return app;
    }

    public static void SetupLocalisation(this WebApplication app)
    {
        var supportedCultures = new[]
        {
            "en-US"
        };

        var localizationOptions = new RequestLocalizationOptions()
            .SetDefaultCulture(supportedCultures[0])
            .AddSupportedCultures(supportedCultures)
            .AddSupportedUICultures(supportedCultures);

        app.UseRequestLocalization(localizationOptions);
    }

    public static async Task PerformAppInitialisation(this WebApplication app)
    {
        // Ensure subfolders are initialised.
        var storage = app.Services.GetRequiredService<ImageStorage>();
        storage.EnsureStorage();

        // Ensure database is initialised.
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        await db.Database.MigrateAsync();
    }

    private static string GetContentType(string extension)
    {
        return extension.ToUpperInvariant() switch
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
