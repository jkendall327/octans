using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Octans.Core;
using Octans.Core.Models;
using Xunit.Abstractions;

namespace Octans.Tests;

public class DeleteEndpointTests(WebApplicationFactory<Client.Program> factory, ITestOutputHelper helper) : EndpointTest(factory, helper)
{
    [Fact]
    public async Task Delete_ExistingFile_ReturnsSuccessAndRemovesFile()
    {
        var hashed = AddFileToFilesystem(out var filePath);

        var id = await AddFileToDatabase(hashed);

        var result = await Api.DeleteFiles(new([id]));

        result.Content!.Results.Single().Success.Should().BeTrue();

        // Ensure it's gone from the filesystem
        FileSystem.FileExists(filePath).Should().BeFalse();

        // Ensure it's marked as deleted in the database
        var deletedHash = await Context.Hashes.FindAsync(id);
        await Context.Entry(deletedHash!).ReloadAsync();

        deletedHash.Should().NotBeNull();
        deletedHash!.DeletedAt.Should().NotBeNull();
    }

    private async Task<int> AddFileToDatabase(HashedBytes hashed)
    {
        var hashItem = new HashItem { Hash = hashed.Bytes };

        Context.Hashes.Add(hashItem);

        await Context.SaveChangesAsync();

        return hashItem.Id;
    }

    private HashedBytes AddFileToFilesystem(out string filePath)
    {
        var fileBytes = TestingConstants.MinimalJpeg;

        var hashed = HashedBytes.FromUnhashed(fileBytes);

        filePath = FileSystem.Path.Combine(AppRoot, "db", "files", hashed.ContentBucket, hashed.Hexadecimal + ".jpeg");

        FileSystem.AddFile(filePath, new(fileBytes));

        return hashed;
    }

    [Fact]
    public async Task Delete_NonExistingFile_ReturnsNotFoundResult()
    {
        var response = await Api.DeleteFiles(new([888]));

        var itemResult = response.Content!.Results.Single();

        itemResult.Success.Should().BeFalse();
        itemResult.Error.Should().NotBeNullOrEmpty();
    }
}