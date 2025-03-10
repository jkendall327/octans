using System.IO.Abstractions;
using System.Threading.Channels;
using Octans.Core;
using Octans.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Octans.Core.Downloaders;
using Octans.Core.Importing;
using Octans.Core.Infrastructure;
using Octans.Core.Querying;
using Octans.Core.Tags;
using Octans.Server.Services;

namespace Octans.Server;

internal static class ServiceCollectionExtensions
{
    public static void AddOptions(this WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration.GetSection("GlobalSettings");
        builder.Services.Configure<GlobalSettings>(configuration);
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
        builder.Services.AddScoped<ImportRouter>();
        builder.Services.AddScoped<SimpleImporter>();
        builder.Services.AddScoped<FileImporter>();
        builder.Services.AddScoped<PostImporter>();

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
        builder.Services.AddScoped<QueryService>();
        builder.Services.AddScoped<QueryParser>();
        builder.Services.AddScoped<QueryPlanner>();
        builder.Services.AddScoped<QueryTagConverter>();
        builder.Services.AddScoped<HashSearcher>();

        // Stats
        builder.Services.AddScoped<StatsService>();
        builder.Services.AddScoped<StorageService>();

        builder.Services.AddMemoryCache();
    }
}
