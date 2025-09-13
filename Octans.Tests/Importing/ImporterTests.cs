using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Threading.Channels;
using Octans.Core.Importing;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Octans.Client;
using Octans.Core;
using Octans.Core.Models;
using Octans.Server;
using Octans.Core.Progress;
using Xunit.Abstractions;

namespace Octans.Tests;

public sealed class ImporterTests : IAsyncLifetime, IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _databaseFixture;
    private const string AppRoot = "/app";

    private readonly IImporter _sut;

    private readonly MockFileSystem _fileSystem = new();
    private readonly SpyChannelWriter<ThumbnailCreationRequest> _spy = new();
    private readonly IServiceProvider _provider;

    public ImporterTests(ITestOutputHelper testOutputHelper, DatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture;
        var services = new ServiceCollection();

        services.AddLogging(s => s.AddProvider(new XUnitLoggerProvider(testOutputHelper)));
        services.AddBusinessServices();
        services.AddSingleton<IBackgroundProgressReporter, NoOpProgressReporter>();

        services.AddDbContext<ServerDbContext>(options => { options.UseSqlite(databaseFixture.Connection); },
            optionsLifetime: ServiceLifetime.Singleton);

        services.AddDbContextFactory<ServerDbContext>();

        services.AddSingleton<IFileSystem>(_fileSystem);

        services.AddSingleton<ChannelWriter<ThumbnailCreationRequest>>(_spy);

        services.AddHttpClient();

        services.Configure<GlobalSettings>(s => s.AppRoot = AppRoot);

        _provider = services.BuildServiceProvider();

        _sut = _provider.GetRequiredService<IImporter>();
    }

    private sealed class NoOpProgressReporter : IBackgroundProgressReporter
    {
        public Task<ProgressHandle> Start(string operation, int totalItems) =>
            Task.FromResult(new ProgressHandle(Guid.NewGuid(), operation, totalItems));

        public Task Report(Guid id, int processed) => Task.CompletedTask;

        public Task Complete(Guid id) => Task.CompletedTask;

        public Task ReportMessage(string message) => Task.CompletedTask;

        public Task ReportError(string message) => Task.CompletedTask;
    }

    [Fact]
    public async Task Import_ValidRequest_ReturnsSuccessResult()
    {
        (var request, var result) = await SendSimpleValidRequest();

        result
            .Should()
            .NotBeNull();

        result
            .ImportId
            .Should()
            .Be(request.ImportId);

        result
            .Results
            .Single()
            .Ok
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task Import_ValidRequest_WritesFileToSubfolder()
    {
        _ = await SendSimpleValidRequest();

        var expectedPath = _fileSystem.Path.Join(AppRoot,
            "db",
            "files",
            "f61",
            "61F461B34DCF8D8227A8691A6625444C1E2C793A181C7D0AD5EF8B15D5E6D040.jpg");

        var file = _fileSystem.GetFile(expectedPath);

        file
            .Should()
            .NotBeNull();
    }

    [Fact]
    public async Task Import_ValidRequest_PersistsInfoToDatabase()
    {
        _ = await SendSimpleValidRequest();

        using var serviceScope = _provider.CreateScope();

        var db = serviceScope.ServiceProvider.GetRequiredService<ServerDbContext>();

        var mapping = db
            .Mappings
            .Include(mapping => mapping.Hash)
            .Include(mapping => mapping.Tag)
            .ThenInclude(tag => tag.Namespace)
            .Include(mapping => mapping.Tag)
            .ThenInclude(tag => tag.Subtag)
            .Single();

        mapping
            .Tag
            .Namespace
            .Value
            .Should()
            .Be("category", "the namespace should be linked the tag");

        mapping
            .Tag
            .Subtag
            .Value
            .Should()
            .Be("example", "the subtag should be linked to the tag");

        var hashed = HashedBytes.FromUnhashed(TestingConstants.MinimalJpeg);

        mapping
            .Hash
            .Hash
            .Should()
            .BeEquivalentTo(hashed.Bytes, "we should be persisting the hashed bytes");
    }

    [Fact]
    public async Task Import_ValidRequest_SendsRequestToThumbnailCreationQueue()
    {
        _ = await SendSimpleValidRequest();

        var thumbnailRequest = _spy.WrittenItems.Single();

        thumbnailRequest
            .Bytes
            .Should()
            .BeEquivalentTo(TestingConstants.MinimalJpeg, "thumbnails should be made for valid imports");
    }

    [Fact]
    public async Task Import_PreviouslyDeletedImage_ShouldNotReimportByDefault()
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();

        var hash = await SetupDeletedImage(db);

        var request = BuildReimportRequest();

        request.AllowReimportDeleted = false;

        var result = await _sut.ProcessImport(request);

        result.Should().NotBeNull();
        result.Results.Single().Ok.Should().BeFalse("we tried to reimport a deleted file when that wasn't allowed");

        var dbHash = await db.Hashes.FindAsync(hash.Id);

        dbHash.Should().NotBeNull("hashes for deleted files remain in the DB to prevent reimports");

        await db.Entry(dbHash).ReloadAsync();

        dbHash!.DeletedAt.Should().NotBeNull("reimporting wasn't allowed, so it should still be marked as deleted");
    }

    [Fact]
    public async Task Import_PreviouslyDeletedImage_ShouldReimportWhenAllowed()
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();

        var hash = await SetupDeletedImage(db);

        var request = BuildReimportRequest();

        request.AllowReimportDeleted = true;

        var result = await _sut.ProcessImport(request);

        result.Should().NotBeNull();
        result.Results.Single().Ok.Should().BeTrue("reimporting the deleted hash was specifically requested");

        var dbHash = await db.Hashes.FindAsync(hash.Id);

        dbHash.Should().NotBeNull();

        // Make sure we don't use the one in the change tracker, as that won't reflect the changes from the API.
        await db.Entry(dbHash).ReloadAsync();

        dbHash.DeletedAt.Should().BeNull("reimporting was allowed, so its soft-deletion mark should be gone");
    }

    private async Task<HashItem> SetupDeletedImage(ServerDbContext db)
    {
        var hash = new HashItem
        {
            Hash = HashedBytes.FromUnhashed(TestingConstants.MinimalJpeg).Bytes,
            DeletedAt = DateTime.UtcNow.AddDays(-1)
        };

        db.Hashes.Add(hash);
        await db.SaveChangesAsync();

        return hash;
    }

    private ImportRequest BuildReimportRequest()
    {
        _fileSystem.AddFile("C:/myfile.jpeg", new(TestingConstants.MinimalJpeg));

        var item = new ImportItem
        {
            Filepath = "C:/myfile.jpeg",
            Tags = [new("test", "reimport")]
        };

        var request = new ImportRequest
        {
            Items = [item],
            ImportType = ImportType.File,
            DeleteAfterImport = false,
            AllowReimportDeleted = false
        };

        return request;
    }

    private async Task<(ImportRequest request, ImportResult Content)> SendSimpleValidRequest()
    {
        var mockFile = new MockFileData(TestingConstants.MinimalJpeg);

        var filepath = _fileSystem.Path.Join(AppRoot, "image.jpg");

        _fileSystem.AddFile(filepath, mockFile);

        var request = BuildRequest(filepath, "category", "example");

        var response = await _sut.ProcessImport(request);

        return (request, response);
    }

    private static ImportRequest BuildRequest(string source, string @namespace, string subtag)
    {
        var tag = new TagModel(Namespace: @namespace, Subtag: subtag);

        var item = new ImportItem
        {
            Filepath = source,
            Tags = [tag]
        };

        var request = new ImportRequest
        {
            Items = [item],
            ImportType = ImportType.File,
            DeleteAfterImport = false
        };

        return request;
    }

    public async Task InitializeAsync()
    {
        await _databaseFixture.ResetAsync(_provider);

        var folders = _provider.GetRequiredService<SubfolderManager>();

        folders.MakeSubfolders();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }
}