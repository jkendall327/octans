using System.IO.Abstractions.TestingHelpers;
using System.Net.Http.Json;
using FluentAssertions;
using HydrusReplacement.Core;
using HydrusReplacement.Core.Importing;
using HydrusReplacement.Core.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace HydrusReplacement.IntegrationTests;

public class ReimportTests : EndpointTest
{
    public ReimportTests(WebApplicationFactory<Program> factory) : base(factory)
    {

    }

    [Fact]
    public async Task Import_PreviouslyDeletedImage_ShouldNotReimportByDefault()
    {
        var hash = await SetupDeletedImage();
        
        var request = BuildRequest();

        request.AllowReimportDeleted = false;
        
        var result = await SendRequest(request);

        result.Should().NotBeNull();
        result!.Results.Single().Ok.Should().BeFalse();

        var dbHash = await _context.Hashes.FindAsync(hash.Id);
        await _context.Entry(dbHash).ReloadAsync();

        dbHash.Should().NotBeNull();
    }

    [Fact]
    public async Task Import_PreviouslyDeletedImage_ShouldReimportWhenAllowed()
    {
        var hash = await SetupDeletedImage();

        var request = BuildRequest();

        request.AllowReimportDeleted = true;

        var result = await SendRequest(request);
        
        result.Should().NotBeNull();
        result!.Results.Single().Ok.Should().BeTrue();
        
        // Make sure we don't use the one in the change tracker, as that won't reflect the changes from the API.
        var dbHash = await _context.Hashes.FindAsync(hash.Id) ?? throw new InvalidOperationException("Should always exist");
        await _context.Entry(dbHash).ReloadAsync();
        
        dbHash.DeletedAt.Should().BeNull();
    }

    private async Task<HashItem> SetupDeletedImage()
    {
        var hash = new HashItem
        {
            Hash = new HashedBytes(TestingConstants.MinimalJpeg, ItemType.File).Bytes,
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
            // Need to use the mock filesystem to point at a file made with the minimal JPEG.
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