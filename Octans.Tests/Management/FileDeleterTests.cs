using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Octans.Client;
using Octans.Core;
using Octans.Core.Deletion;
using Octans.Core.Filesystem;
using Octans.Data.Models;
using Octans.Tests.Helpers;
using Xunit.Abstractions;

namespace Octans.Tests.Management;

public class FileDeleterTests : IAsyncLifetime, IClassFixture<DatabaseFixture>
{
    private const string AppRoot = "/app";
    private readonly FileDeleter _sut;

    private readonly IServiceProvider _provider;
    private readonly DatabaseFixture _databaseFixture;
    private readonly MockFileSystem _fileSystem = new();

    public FileDeleterTests(ITestOutputHelper testOutputHelper, DatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture;
        var services = new ServiceCollection();

        services.AddLogging(s => s.AddProvider(new XUnitLoggerProvider(testOutputHelper)));
        services.AddBusinessServices();

        services.AddDbContext<ServerDbContext>(options => { options.UseSqlite(databaseFixture.Connection); },
            optionsLifetime: ServiceLifetime.Singleton);
        services.AddDbContextFactory<ServerDbContext>();

        services.AddSingleton<IFileSystem>(_fileSystem);

        services.Configure<GlobalSettings>(s => s.AppRoot = AppRoot);

        _provider = services.BuildServiceProvider();

        _sut = _provider.GetRequiredService<FileDeleter>();
    }

    [Fact]
    public async Task Delete_ExistingFile_ReturnsSuccessAndRemovesFile()
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();

        var hash = AddFileToFilesystem(out var filePath, out var metadata);

        var id = await AddFileToDatabase(hash, metadata, db);

        var result = await _sut.ProcessDeletion([id]);

        result.Single().Success.Should().BeTrue();

        // Ensure it's gone from the filesystem
        _fileSystem.FileExists(filePath).Should().BeFalse();

        // Ensure it's marked as deleted in the database
        var deletedHash = await db.Hashes.FindAsync(id);
        await db.Entry(deletedHash!).ReloadAsync();

        deletedHash.Should().NotBeNull();
        deletedHash.DeletedAt.Should().NotBeNull();
    }

    private static async Task<int> AddFileToDatabase(ContentHash hash, ImageMetadata metadata, ServerDbContext db)
    {
        var hashItem = new HashItem
        {
            Hash = hash.Bytes,
            Extension = metadata.Extension,
            ContentType = metadata.ContentType
        };

        db.Hashes.Add(hashItem);
        await db.SaveChangesAsync();

        return hashItem.Id;
    }

    private ContentHash AddFileToFilesystem(out string filePath, out ImageMetadata metadata)
    {
        var fileBytes = TestingConstants.MinimalJpeg;

        var hash = ContentHash.FromContent(fileBytes);
        metadata = new ImageMetadata("jpeg", "image/jpeg");

        filePath = _fileSystem.Path.Combine(AppRoot, "db", "files", hash.ContentBucket, hash.Hex + ".jpeg");

        _fileSystem.AddFile(filePath, new(fileBytes));

        return hash;
    }

    [Fact]
    public async Task Delete_NonExistingFile_ReturnsNotFoundResult()
    {
        var response = await _sut.ProcessDeletion([888]);

        var result = response.Single();

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    public async Task InitializeAsync()
    {
        await DatabaseFixture.ResetAsync(_provider);

        var folders = _provider.GetRequiredService<ImageStorage>();

        folders.EnsureStorage();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }
}
