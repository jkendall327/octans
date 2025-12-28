using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Threading.Channels;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Octans.Client;
using Octans.Core;
using Octans.Core.Importing;
using Octans.Core.Models;
using Octans.Core.Progress;
using Octans.Core.Tags;
using Octans.Server;
using Octans.Server.Services;
using Xunit.Abstractions;

namespace Octans.Tests;

public class MultiEndpointIntegrationTests : IAsyncLifetime, IClassFixture<DatabaseFixture>
{
    private const string AppRoot = "/app";
    private readonly DatabaseFixture _databaseFixture;
    private readonly IServiceProvider _provider;
    private readonly MockFileSystem _fileSystem = new();
    private readonly IImporter _importer;
    private readonly TagUpdater _tagUpdater;
    private readonly FileDeleter _fileDeleter;
    private readonly SpyChannelWriter<ThumbnailCreationRequest> _spy = new();

    public MultiEndpointIntegrationTests(ITestOutputHelper helper, DatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture;
        var services = new ServiceCollection();

        services.AddLogging(s => s.AddProvider(new XUnitLoggerProvider(helper)));

        services.AddBusinessServices();

        // Register services not covered by AddBusinessServices or needing mocks
        services.AddSingleton<IBackgroundProgressReporter, NoOpProgressReporter>();
        services.AddSingleton<IFileSystem>(_fileSystem);
        services.AddSingleton<ChannelWriter<ThumbnailCreationRequest>>(_spy);
        services.AddHttpClient();

        services.Configure<GlobalSettings>(s => s.AppRoot = AppRoot);

        // Register Database
        databaseFixture.RegisterDbContext(services);

        _provider = services.BuildServiceProvider();

        _importer = _provider.GetRequiredService<IImporter>();
        _tagUpdater = _provider.GetRequiredService<TagUpdater>();
        _fileDeleter = _provider.GetRequiredService<FileDeleter>();
    }

    [Fact]
    public async Task ImportUpdateAndDeleteImage_ShouldSucceed()
    {
        var imagePath = "C:/test_image.jpg";
        _fileSystem.AddFile(imagePath, new(TestingConstants.MinimalJpeg));

        var expectedFilePath = _fileSystem.Path.Join(AppRoot,
            "db",
            "files",
            "f61",
            "61F461B34DCF8D8227A8691A6625444C1E2C793A181C7D0AD5EF8B15D5E6D040.jpg");

        await ImportFile(imagePath, expectedFilePath);

        await using var scope = _provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ServerDbContext>();

        var hashItem = await context.Hashes.SingleAsync();
        var hashId = hashItem.Id;

        await UpdateTags(hashId, context);

        await DeleteItem(hashId, expectedFilePath, context);
    }

    private async Task ImportFile(string imagePath, string expectedFilePath)
    {
        var item = new ImportItem
        {
            Filepath = imagePath,
            Tags = [new("category", "test")]
        };

        var request = new ImportRequest
        {
            Items = [item],
            ImportType = ImportType.File,
            DeleteAfterImport = false
        };

        var result = await _importer.ProcessImport(request);

        result
            .Should()
            .NotBeNull();

        result
            .Results
            .Single()
            .Ok
            .Should()
            .BeTrue("this import has no reason to fail");

        _fileSystem
            .FileExists(expectedFilePath)
            .Should()
            .BeTrue("we write the bytes to the hex bucket on import");
    }

    private async Task UpdateTags(int hashId, ServerDbContext context)
    {
        var updateTagsRequest = new UpdateTagsRequest(hashId,
            [
                new("character", "mario")
            ],
            [
                new("category", "test")
            ]);

        await _tagUpdater.UpdateTags(updateTagsRequest);

        var tags = await context
            .Mappings
            .Where(m => m.Hash.Id == hashId)
            .Select(m => new
            {
                Namespace = m.Tag.Namespace.Value,
                Subtag = m.Tag.Subtag.Value
            })
            .ToListAsync();

        tags
            .Should()
            .ContainSingle(t => t.Namespace == "character" && t.Subtag == "mario",
                "we should have added this tag/mapping");

        tags
            .Should()
            .NotContain(t => t.Namespace == "category" && t.Subtag == "test",
                "we should have removed this tag/mapping");
    }

    private async Task DeleteItem(int hashId, string expectedFilepath, ServerDbContext context)
    {
        var mappings = await context
            .Mappings
            .Where(m => m.Hash.Id == hashId)
            .ToListAsync();

        var result = await _fileDeleter.ProcessDeletion([hashId]);

        result
            .Single()
            .Success
            .Should()
            .BeTrue();

        // Verify deletion in database
        // We have to reload the item so EF doesn't give us the version in its cache
        // which doesn't reflect the SUT setting the DeletedAt flag.
        var hash = await context.Hashes.FindAsync(hashId);

        await context
            .Entry(hash!)
            .ReloadAsync();

        hash
            .Should()
            .NotBeNull("we soft-delete hashes to prevent them being reimported later");

        hash!
            .DeletedAt
            .Should()
            .NotBeNull("we soft-delete items by setting this value to something non-null");

        // Verify removal from filesystem
        _fileSystem
            .FileExists(expectedFilepath)
            .Should()
            .BeFalse("we remove the physical file even for soft-deletes");

        var mappingsAfterDeletion = await context
            .Mappings
            .Where(m => m.Hash.Id == hashId)
            .ToListAsync();

        mappingsAfterDeletion
            .Should()
            .BeEquivalentTo(mappings, "we don't remove mappings for deleted items");
    }

    public async Task InitializeAsync()
    {
        await DatabaseFixture.ResetAsync(_provider);

        var folders = _provider.GetRequiredService<SubfolderManager>();

        folders.MakeSubfolders();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }
}