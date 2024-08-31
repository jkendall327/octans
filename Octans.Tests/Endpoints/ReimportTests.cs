using System.IO.Abstractions.TestingHelpers;
using System.Net.Http.Json;
using FluentAssertions;
using Octans.Core;
using Octans.Core.Importing;
using Octans.Core.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Octans.Tests;

public class ReimportTests(WebApplicationFactory<Program> factory) : EndpointTest(factory)
{
    [Fact]
    public async Task Import_PreviouslyDeletedImage_ShouldNotReimportByDefault()
    {
        var hash = await SetupDeletedImage();
        
        var request = BuildRequest();

        request.AllowReimportDeleted = false;
        
        var result = await SendRequest(request);

        result.Should().NotBeNull();
        result!.Results.Single().Ok.Should().BeFalse("we tried to reimport a deleted file when that wasn't allowed");

        var dbHash = await _context.Hashes.FindAsync(hash.Id);

        dbHash.Should().NotBeNull("hashes for deleted files remain in the DB to prevent reimports");

        await _context.Entry(dbHash!).ReloadAsync();

        dbHash!.DeletedAt.Should().NotBeNull("reimporting wasn't allowed, so it should still be marked as deleted");
    }

    [Fact]
    public async Task Import_PreviouslyDeletedImage_ShouldReimportWhenAllowed()
    {
        var hash = await SetupDeletedImage();

        var request = BuildRequest();

        request.AllowReimportDeleted = true;

        var result = await SendRequest(request);
        
        result.Should().NotBeNull();
        result!.Results.Single().Ok.Should().BeTrue("reimporting the deleted hash was specifically requested");
        
        var dbHash = await _context.Hashes.FindAsync(hash.Id);
        
        dbHash.Should().NotBeNull();

        // Make sure we don't use the one in the change tracker, as that won't reflect the changes from the API.
        await _context.Entry(dbHash!).ReloadAsync();
        
        dbHash!.DeletedAt.Should().BeNull("reimporting was allowed, so its soft-deletion mark should be gone");
    }

    private async Task<HashItem> SetupDeletedImage()
    {
        var hash = new HashItem
        {
            Hash = HashedBytes.FromUnhashed(TestingConstants.MinimalJpeg).Bytes,
            DeletedAt = DateTime.UtcNow.AddDays(-1)
        };

        _context.Hashes.Add(hash);
        await _context.SaveChangesAsync();

        return hash;
    }
    
    private ImportRequest BuildRequest()
    {
        _fileSystem.AddFile("C:/myfile.jpeg", new(TestingConstants.MinimalJpeg));

        var item = new ImportItem
        {
            Source = new("C:/myfile.jpeg"),
            Tags = [new() { Namespace = "test", Subtag = "reimport" }]
        };
        
        var request = new ImportRequest
        {
            Items = [item],
            DeleteAfterImport = false,
            AllowReimportDeleted = false 
        };
        
        return request;
    }
    
    private async Task<ImportResult?> SendRequest(ImportRequest request)
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/import", request);

        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadFromJsonAsync<ImportResult>();
    }
}