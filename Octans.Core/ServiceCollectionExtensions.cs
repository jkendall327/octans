using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

namespace Octans.Core;

public static class ServiceCollectionExtensions
{
    private static readonly HashSet<Type> OctansBackgroundWorkerTypes =
    [
        typeof(ImportFolderBackgroundService),
        typeof(ImportProcessorService),
        typeof(SubscriptionBackgroundService),
        typeof(RepositoryChangeBackgroundService),
        typeof(DownloadBackgroundService),
        typeof(ThumbnailCreationBackgroundService)
    ];

    public static IServiceCollection AddOctansCoreServices(
        this IServiceCollection services,
        Action<OctansCoreServiceOptions>? configure = null)
    {
        var options = new OctansCoreServiceOptions();
        configure?.Invoke(options);

        services.AddOctansChannels();

        if (options.RegisterBackgroundWorkers)
        {
            services.AddHostedService<ImportFolderBackgroundService>();
            services.AddHostedService<ImportProcessorService>();
            services.AddHostedService<SubscriptionBackgroundService>();
            services.AddHostedService<RepositoryChangeBackgroundService>();
        }

        services.AddSingleton<IBackgroundProgressReporter, BackgroundProgressService>();
        services.AddScoped<ImportFolderScanner>();
        services.AddSingleton<RepositoryChangeProcessor>();

        services.AddBusinessServices(options.RegisterDownloadBackgroundWorker);

        return services;
    }

    public static IServiceCollection AddOctansChannels(this IServiceCollection services)
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

        return services;
    }

    public static IServiceCollection RemoveOctansBackgroundWorkers(this IServiceCollection services)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            var descriptor = services[i];
            if (descriptor.ServiceType != typeof(IHostedService) ||
                descriptor.ImplementationType is not { } implementationType ||
                !OctansBackgroundWorkerTypes.Contains(implementationType))
            {
                continue;
            }

            services.RemoveAt(i);
        }

        return services;
    }

    public static IServiceCollection AddBusinessServices(
        this IServiceCollection services,
        bool registerDownloadBackgroundWorker = true)
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
        services.AddOptions<DownloaderResolverOptions>();
        services.AddScoped<DownloaderFactory>();
        services.AddScoped<IDownloaderFactory>(s => s.GetRequiredService<DownloaderFactory>());
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
        services.AddSingleton<IRobustFileWriter, RobustFileWriter>();
        services.AddBandwidthLimiter();
        services.AddDownloadManager(registerBackgroundService: registerDownloadBackgroundWorker);

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
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<ISubscriptionExecutor, NoOpSubscriptionExecutor>();

        services.AddMemoryCache();

        return services;
    }
}

public sealed class OctansCoreServiceOptions
{
    public bool RegisterBackgroundWorkers { get; set; } = true;
    public bool RegisterDownloadBackgroundWorker { get; set; } = true;
}
