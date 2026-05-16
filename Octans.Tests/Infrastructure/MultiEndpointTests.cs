using System.Threading.Channels;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Octans.Core;
using Octans.Core.Filesystem;
using Octans.Core.Importing;
using Octans.Core.Progress;
using Octans.Core.Tags;
using Octans.Core.Thumbnails;
using Octans.Data.Models;
using Octans.Tests.Helpers;
using Xunit.Abstractions;

namespace Octans.Tests.Infrastructure;

public class MultiEndpointIntegrationTests : IAsyncLifetime, IClassFixture<DatabaseFixture>
{
    private readonly OctansTestHost _host;
    private readonly IImporter _importer;
    private readonly TagUpdater _tagUpdater;
    private readonly FileDeleter _fileDeleter;
    private readonly SpyChannelWriter<ThumbnailCreationRequest> _spy = new();

    public MultiEndpointIntegrationTests(ITestOutputHelper helper, DatabaseFixture databaseFixture)
    {
        _host = OctansTestHost.Create(
            helper,
            databaseFixture,
            services =>
            {
                services.AddSingleton<IBackgroundProgressReporter, NoOpProgressReporter>();
                services.AddSingleton<ChannelWriter<ThumbnailCreationRequest>>(_spy);
                services.AddHttpClient();
            });

        _importer = _host.GetRequiredService<IImporter>();
        _tagUpdater = _host.GetRequiredService<TagUpdater>();
        _fileDeleter = _host.GetRequiredService<FileDeleter>();
    }

    [Fact]
    public async Task ImportUpdateAndDeleteImage_ShouldSucceed()
    {
        var imagePath = "C:/test_image.jpg";
        _host.FileSystem.AddFile(imagePath, new(TestingConstants.MinimalJpeg));

        var imageStorage = _host.GetRequiredService<ImageStorage>();
        var hash = ContentHash.FromContent(TestingConstants.MinimalJpeg);
        var metadata = imageStorage.GetMetadata(TestingConstants.MinimalJpeg);
        var expectedFilePath = imageStorage.GetOriginalDestination(hash, metadata);

        await ImportFile(imagePath, expectedFilePath);

        await using var scope = _host.CreateAsyncScope();
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

        _host.FileSystem
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
        _host.FileSystem
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
        await _host.ResetDatabaseAsync();
        _host.EnsureImageStorage();
    }

    public Task DisposeAsync()
    {
        return _host.DisposeAsync().AsTask();
    }
}
