using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Octans.Client;
using Octans.Core;
using Octans.Core.Filesystem;
using Octans.Core.Importing;
using Octans.Core.Repositories;
using Octans.Data.Models;
using Octans.Tests.Helpers;
using Xunit.Abstractions;

namespace Octans.Tests.Infrastructure;

public sealed class OctansApiFactory : WebApplicationFactory<Program>
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    private readonly SqliteConnection _keepAliveConnection;
    private readonly Action<IServiceCollection>? _configureTestServices;
    private readonly ITestOutputHelper? _output;

    public OctansApiFactory(
        ITestOutputHelper? output = null,
        Action<IServiceCollection>? configureTestServices = null)
    {
        _output = output;
        _configureTestServices = configureTestServices;
        AppRoot = $"/octans-api-{Guid.NewGuid():N}";
        FileSystem = new();
        TimeProvider = new(TestClock.UtcNow);

        var connectionString = $"Data Source=OctansApiFactory-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAliveConnection = new(connectionString);
        _keepAliveConnection.Open();
        ConnectionString = connectionString;
    }

    public string AppRoot { get; }
    public MockFileSystem FileSystem { get; }
    public FakeTimeProvider TimeProvider { get; }
    public string ConnectionString { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GlobalSettings:AppRoot"] = AppRoot,
                ["Octans:BackgroundWorkers:Enabled"] = "false",
                ["ImportFolder:Enabled"] = "false"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveOctansBackgroundWorkers();

            services.RemoveAll<ServerDbContext>();
            services.RemoveAll<DbContextOptions<ServerDbContext>>();
            services.RemoveAll<IDbContextFactory<ServerDbContext>>();

            services.AddDbContextFactory<ServerDbContext>(options => options.UseSqlite(ConnectionString));
            services.AddDbContext<ServerDbContext>(
                options => options.UseSqlite(ConnectionString),
                optionsLifetime: ServiceLifetime.Singleton);

            services.RemoveAll<IFileSystem>();
            services.AddSingleton<IFileSystem>(FileSystem);

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(TimeProvider);

            services.Configure<GlobalSettings>(settings => settings.AppRoot = AppRoot);

            if (_output is not null)
            {
                services.AddLogging(logging => logging.AddProvider(new XUnitLoggerProvider(_output)));
            }

            _configureTestServices?.Invoke(services);
        });
    }

    public AsyncServiceScope CreateAsyncScope()
    {
        return Services.CreateAsyncScope();
    }

    public static async Task<FileQueryResult> QueryAsync(HttpClient client, IReadOnlyList<string> query)
    {
        var response = await client.PostAsJsonAsync(
            new Uri("/files/query", UriKind.Relative),
            query,
            JsonOptions);

        var items = await response.Content.ReadFromJsonAsync<List<HashItem>>(JsonOptions) ?? [];

        return new(response, items);
    }

    public static async Task<FileQueryCountResult> CountQueryAsync(HttpClient client, IReadOnlyList<string> query)
    {
        var response = await client.PostAsJsonAsync(
            new Uri("/files/query/count", UriKind.Relative),
            query,
            JsonOptions);
        var count = await response.Content.ReadFromJsonAsync<FileQueryCountDto>(JsonOptions);

        return new(response, count?.Count ?? 0);
    }

    public Task<bool> ProcessQueuedImportJobAsync(CancellationToken cancellationToken = default)
    {
        var processor = new ImportProcessorService(
            Services,
            NullLogger<ImportProcessorService>.Instance);

        return processor.ProcessQueuedJob(cancellationToken);
    }

    public async Task ProcessNextRepositoryChangeAsync(CancellationToken cancellationToken = default)
    {
        var reader = Services.GetRequiredService<ChannelReader<RepositoryChangeRequest>>();
        var request = await reader.ReadAsync(cancellationToken);
        var processor = Services.GetRequiredService<RepositoryChangeProcessor>();

        await processor.ProcessBatch([request], cancellationToken);
    }

    public async Task<StoredMedia> AddStoredImageAsync(
        byte[] bytes,
        RepositoryType repository = RepositoryType.Inbox,
        ulong? perceptualHash = null,
        ImageMetadata? metadata = null)
    {
        await using var scope = CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        await db.Database.EnsureCreatedAsync();

        var stored = await AddStoredImageAsync(db, bytes, repository, perceptualHash, metadata);
        await db.SaveChangesAsync();

        return stored;
    }

    public async Task<StoredMedia> AddStoredImageAsync(
        ServerDbContext db,
        byte[] bytes,
        RepositoryType repository = RepositoryType.Inbox,
        ulong? perceptualHash = null,
        ImageMetadata? metadata = null)
    {
        await db.Database.EnsureCreatedAsync();

        var imageStorage = Services.GetRequiredService<ImageStorage>();
        imageStorage.EnsureStorage();

        var hash = ContentHash.FromContent(bytes);
        metadata ??= imageStorage.GetMetadata(bytes);
        var path = imageStorage.GetOriginalDestination(hash, metadata);
        await FileSystem.File.WriteAllBytesAsync(path, bytes);

        var entity = new HashItem
        {
            Hash = hash.Bytes,
            Extension = metadata.Extension,
            ContentType = metadata.ContentType,
            RepositoryId = (int)repository,
            PerceptualHash = perceptualHash
        };
        db.Hashes.Add(entity);

        return new(hash, metadata, path, entity);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _keepAliveConnection.Dispose();
        }
    }
}

public sealed record FileQueryResult(HttpResponseMessage Response, IReadOnlyList<HashItem> Items)
{
    public IReadOnlyList<string> Hashes { get; } = Items
        .Select(item => ContentHash.FromHashBytes(item.Hash).Hex)
        .ToArray();
}

public sealed record FileQueryCountResult(HttpResponseMessage Response, int Count);

public sealed record StoredMedia(ContentHash Hash, ImageMetadata Metadata, string Path, HashItem Entity);
