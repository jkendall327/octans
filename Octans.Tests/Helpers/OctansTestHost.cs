using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Octans.Client;
using Octans.Core;
using Octans.Core.Filesystem;
using Octans.Data.Models;
using Xunit.Abstractions;

namespace Octans.Tests.Helpers;

public sealed class OctansTestHost : IAsyncDisposable
{
    public const string DefaultAppRoot = "/app";

    private readonly ServiceProvider _provider;

    private OctansTestHost(
        ServiceProvider provider,
        MockFileSystem fileSystem,
        FakeTimeProvider timeProvider,
        string appRoot)
    {
        _provider = provider;
        FileSystem = fileSystem;
        TimeProvider = timeProvider;
        AppRoot = appRoot;
    }

    public IServiceProvider Services => _provider;
    public MockFileSystem FileSystem { get; }
    public FakeTimeProvider TimeProvider { get; }
    public string AppRoot { get; }

    public static OctansTestHost Create(
        ITestOutputHelper testOutputHelper,
        DatabaseFixture databaseFixture,
        Action<IServiceCollection>? configureServices = null,
        string appRoot = DefaultAppRoot,
        ServiceLifetime dbLifetime = ServiceLifetime.Singleton,
        bool addBusinessServices = true)
    {
        var services = new ServiceCollection();

        services.AddLogging(s => s.AddProvider(new XUnitLoggerProvider(testOutputHelper)));

        if (addBusinessServices)
        {
            services.AddBusinessServices();
        }

        databaseFixture.RegisterDbContext(services, dbLifetime);

        var fileSystem = new MockFileSystem();
        var timeProvider = new FakeTimeProvider(TestClock.UtcNow);

        services.ReplaceExistingRegistrationsWith<IFileSystem>(fileSystem);
        services.ReplaceExistingRegistrationsWith<TimeProvider>(timeProvider);
        services.Configure<GlobalSettings>(s => s.AppRoot = appRoot);

        configureServices?.Invoke(services);

        var provider = services.BuildServiceProvider();

        return new(provider, fileSystem, timeProvider, appRoot);
    }

    public T GetRequiredService<T>() where T : notnull
    {
        return _provider.GetRequiredService<T>();
    }

    public AsyncServiceScope CreateAsyncScope()
    {
        return _provider.CreateAsyncScope();
    }

    public IServiceScope CreateScope()
    {
        return _provider.CreateScope();
    }

    public async Task ResetDatabaseAsync()
    {
        await DatabaseFixture.ResetAsync(_provider);
    }

    public void EnsureImageStorage()
    {
        GetRequiredService<ImageStorage>().EnsureStorage();
    }

    public async Task<StoredImage> AddStoredImageAsync(
        byte[] bytes,
        ImageMetadata metadata,
        ServerDbContext? dbContext = null)
    {
        var hash = ContentHash.FromContent(bytes);
        var hashItem = new HashItem
        {
            Hash = hash.Bytes,
            Extension = metadata.Extension,
            ContentType = metadata.ContentType
        };

        var db = dbContext ?? GetRequiredService<ServerDbContext>();
        db.Hashes.Add(hashItem);
        await db.SaveChangesAsync();

        var path = GetRequiredService<ImageStorage>().GetOriginalDestination(hash, metadata);
        FileSystem.AddFile(path, new MockFileData(bytes));

        return new(hashItem, hash, metadata, path);
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
    }
}

public sealed record StoredImage(
    HashItem HashItem,
    ContentHash Hash,
    ImageMetadata Metadata,
    string Path);
