using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Octans.Core;
using Octans.Core.Models;

namespace Octans.Tests;

public class DeleteEndpointTests : EndpointTest
{
    public DeleteEndpointTests(WebApplicationFactory<Program> factory) : base(factory) { }

    [Fact]
    public async Task Delete_ExistingFile_ReturnsSuccessAndRemovesFile()
    {
        // Add file to filesystem
        var fileBytes = TestingConstants.MinimalJpeg;
        var hashed = new HashedBytes(fileBytes, ItemType.File);
        var filePath = _fileSystem.Path.Combine(_appRoot, "db", "files", hashed.Bucket, $"{hashed.Hexadecimal}.jpg");
        _fileSystem.AddFile(filePath, new(fileBytes));

        // Add file to database
        var hashItem = new HashItem { Hash = hashed.Bytes };
        _context.Hashes.Add(hashItem);
        await _context.SaveChangesAsync();

        var result = await SendDeletionRequest(hashItem.Id);

        result.Results.Single().Success.Should().BeTrue();

        // Ensure it's gone from the filesystem
        _fileSystem.FileExists(filePath).Should().BeFalse();

        // Ensure it's marked as deleted in the database
        var deletedHash = await _context.Hashes.FindAsync(hashItem.Id);
        deletedHash.Should().NotBeNull();
        await _context.Entry(deletedHash!).ReloadAsync();
        deletedHash!.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Delete_NonExistingFile_ReturnsNotFoundResult()
    {
        var result = await SendDeletionRequest(888);

        var itemResult = result.Results.Single();
        
        itemResult.Success.Should().BeFalse();
        itemResult.Error.Should().NotBeNullOrEmpty();
    }

    private async Task<DeleteResponse> SendDeletionRequest(int imageId)
    {
        var client = _factory.CreateClient();
        var request = new DeleteRequest(new List<DeleteItem> { new(imageId) });

        var response = await client.PostAsJsonAsync("/delete", request);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<DeleteResponse>();

        if (result is null)
        {
            throw new InvalidOperationException("Deserializing the API response failed");
        }
        
        return result;
    }
}