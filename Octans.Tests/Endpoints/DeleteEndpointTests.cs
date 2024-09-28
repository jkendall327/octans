using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Octans.Core;
using Octans.Core.Models;
using Octans.Server.Services;
using Xunit.Abstractions;

namespace Octans.Tests;

public class DeleteEndpointTests(WebApplicationFactory<Program> factory, ITestOutputHelper helper) : EndpointTest(factory, helper)
{
    [Fact]
    public async Task Delete_ExistingFile_ReturnsSuccessAndRemovesFile()
    {
        // Add file to filesystem
        var fileBytes = TestingConstants.MinimalJpeg;
        var hashed = HashedBytes.FromUnhashed(fileBytes);
        var filePath = _fileSystem.Path.Combine(_appRoot, "db", "files", hashed.ContentBucket, hashed.Hexadecimal + ".jpeg");
        _fileSystem.AddFile(filePath, new(fileBytes));

        // Add file to database
        var hashItem = new HashItem { Hash = hashed.Bytes };
        _context.Hashes.Add(hashItem);
        await _context.SaveChangesAsync();

        var result = await _api.DeleteFiles([hashItem.Id]);

        result.Content!.Results.Single().Success.Should().BeTrue();

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
        var ids = new List<int>()
        {
            999, 345, 3
        };
        var response = await _api.DeleteFiles(null);

        var itemResult = response.Content!.Results.Single();
        
        itemResult.Success.Should().BeFalse();
        itemResult.Error.Should().NotBeNullOrEmpty();
    }
}