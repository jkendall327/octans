using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Octans.Core;
using Octans.Core.Importing;
using Xunit.Abstractions;

namespace Octans.Tests;

public class MultiEndpointIntegrationTests(WebApplicationFactory<Program> factory, ITestOutputHelper helper) : EndpointTest(factory, helper)
{
    [Fact]
    public async Task ImportUpdateAndDeleteImage_ShouldSucceed()
    {
        // Add a test file.
        var imagePath = "C:/test_image.jpg";
        _fileSystem.AddFile(imagePath, new(TestingConstants.MinimalJpeg));

        var expectedFilePath = _fileSystem.Path.Join(_appRoot,
            "db",
            "files",
            "f61",
            "61F461B34DCF8D8227A8691A6625444C1E2C793A181C7D0AD5EF8B15D5E6D040.jpg");
        
        await ImportFile(imagePath, expectedFilePath);

        var hashItem = await _context.Hashes.SingleAsync();
        var hashId = hashItem.Id;

        await UpdateTags(hashId);

        await DeleteItem(hashId, expectedFilePath);
    }

    private async Task ImportFile(string imagePath, string expectedFilePath)
    {
        var client = _factory.CreateClient();

        var item = new ImportItem
        {
            Source = new(imagePath),
            Tags = [ new() { Namespace = "category", Subtag = "test" } ]
        };
        
        var request = new ImportRequest
        {
            Items = [item],
            DeleteAfterImport = false
        };

        var response = await client.PostAsJsonAsync("/import", request);
        
        response.EnsureSuccessStatusCode();
        
        var result = await response.Content.ReadFromJsonAsync<ImportResult>();
        
        result.Should().NotBeNull();
        result!.Results.Single().Ok.Should().BeTrue("this import has no reason to fail");
        
        _fileSystem.FileExists(expectedFilePath).Should().BeTrue("we write the bytes to the hex bucket on import");
    }
    
    private async Task UpdateTags(int hashId)
    {
        var client = _factory.CreateClient();

        var updateTagsRequest = new UpdateTagsRequest
        {
            HashId = hashId,
            TagsToAdd = [new() { Namespace = "character", Subtag = "mario" }],
            TagsToRemove = [new() { Namespace = "category", Subtag = "test" }]
        };

        var updateResponse = await client.PutAsJsonAsync("/updateTags", updateTagsRequest);
        
        updateResponse.EnsureSuccessStatusCode();

        var tags = await _context.Mappings
            .Where(m => m.Hash.Id == hashId)
            .Select(m => new { Namespace = m.Tag.Namespace.Value, Subtag = m.Tag.Subtag.Value })
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
    
    private async Task DeleteItem(int hashId, string expectedFilepath)
    {
        var client = _factory.CreateClient();

        var request = new DeleteRequest([new(hashId)]);

        var mappings = await _context.Mappings.Where(m => m.Hash.Id == hashId).ToListAsync();
        
        var response = await client.PostAsJsonAsync("/delete", request);
        
        response.EnsureSuccessStatusCode();
        
        var result = await response.Content.ReadFromJsonAsync<DeleteResponse>();
        
        result.Should().NotBeNull();
        result!.Results.Single().Success.Should().BeTrue();

        // Verify deletion in database
        // We have to reload the item so EF doesn't give us the version in its cache
        // which doesn't reflect the SUT setting the DeletedAt flag.
        var hash = await _context.Hashes.FindAsync(hashId);
        await _context.Entry(hash!).ReloadAsync();
        
        hash.Should().NotBeNull("we soft-delete hashes to prevent them being reimported later");
        hash!.DeletedAt.Should().NotBeNull("we soft-delete items by setting this value to something non-null");

        // Verify removal from filesystem
        _fileSystem.FileExists(expectedFilepath)
            .Should()
            .BeFalse("we remove the physical file even for soft-deletes");
        
        var mappingsAfterDeletion = await _context.Mappings
            .Where(m => m.Hash.Id == hashId)
            .ToListAsync();

        mappingsAfterDeletion.Should().BeEquivalentTo(mappings, "we don't remove mappings for deleted items");
    }
}