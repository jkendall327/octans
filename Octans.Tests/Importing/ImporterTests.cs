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
            Source = new(source),
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