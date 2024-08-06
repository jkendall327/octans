using System.IO.Abstractions.TestingHelpers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Octans.Core;
using Octans.Core.Importing;
using Octans.Core.Models;

namespace Octans.Tests;

public class MultiEndpointIntegrationTests : EndpointTest
{
    public MultiEndpointIntegrationTests(WebApplicationFactory<Program> factory) : base(factory) { }

    [Fact]
    public async Task ImportUpdateAndDeleteImage_ShouldSucceed()
    {
        // Arrange
        var client = _factory.CreateClient();
        var imagePath = "C:/test_image.jpg";
        _fileSystem.AddFile(imagePath, new MockFileData(TestingConstants.MinimalJpeg));

        // Import
        var importRequest = new ImportRequest
        {
            Items =
            [
                new()
                {
                    Source = new Uri(imagePath),
                    Tags =
                    [
                        new() { Namespace = "category", Subtag = "test" }
                    ]
                }
            ],
            DeleteAfterImport = false
        };

        var importResponse = await client.PostAsJsonAsync("/import", importRequest);
        importResponse.EnsureSuccessStatusCode();
        var importResult = await importResponse.Content.ReadFromJsonAsync<ImportResult>();
        importResult.Should().NotBeNull();
        importResult!.Results.Single().Ok.Should().BeTrue();

        // Get the HashId
        var hashItem = await _context.Hashes.SingleAsync();
        var hashId = hashItem.Id;

        // Update Tags
        var updateTagsRequest = new UpdateTagsRequest
        {
            HashId = hashId,
            TagsToAdd = [new() { Namespace = "character", Subtag = "mario" }],
            TagsToRemove = [new() { Namespace = "category", Subtag = "test" }]
        };

        var updateResponse = await client.PutAsJsonAsync("/updateTags", updateTagsRequest);
        updateResponse.EnsureSuccessStatusCode();

        // Verify tag update
        var updatedTags = await _context.Mappings
            .Where(m => m.Hash.Id == hashId)
            .Select(m => new { Namespace = m.Tag.Namespace.Value, Subtag = m.Tag.Subtag.Value })
            .ToListAsync();

        updatedTags.Should().ContainSingle(t => t.Namespace == "character" && t.Subtag == "mario");
        updatedTags.Should().NotContain(t => t.Namespace == "category" && t.Subtag == "test");

        // Delete
        var deleteRequest = new DeleteRequest([new(hashId)]);
        var deleteResponse = await client.PostAsJsonAsync("/delete", deleteRequest);
        deleteResponse.EnsureSuccessStatusCode();
        var deleteResult = await deleteResponse.Content.ReadFromJsonAsync<DeleteResponse>();
        deleteResult.Should().NotBeNull();
        deleteResult!.Results.Single().Success.Should().BeTrue();

        // Verify deletion
        var deletedHash = await _context.Hashes.FindAsync(hashId);
        deletedHash.Should().NotBeNull();
        await _context.Entry(deletedHash!).ReloadAsync();
        deletedHash!.DeletedAt.Should().NotBeNull();

        // Verify file removal
        var expectedFilePath = _fileSystem.Path.Join(_appRoot, "db", "files", "f61", "61F461B34DCF8D8227A8691A6625444C1E2C793A181C7D0AD5EF8B15D5E6D040.jpg");
        _fileSystem.FileExists(expectedFilePath).Should().BeFalse();
    }
}