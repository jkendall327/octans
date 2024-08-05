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
        var db = _factory.Services.CreateScope().ServiceProvider.GetRequiredService<ServerDbContext>();

        var hash = await SetupDeletedImage(db);
        
        _fileSystem.AddFile("C:/myfile.jpeg", new(TestingConstants.MinimalJpeg));
        
        var request = new ImportRequest
        {
            Items = new()
            {
                new()
                {
                    // Need to use the mock filesystem to point at a file made with the minimal JPEG.
                    Source = new("C:/myfile.jpeg"),
                    Tags = new[] { new TagModel { Namespace = "test", Subtag = "reimport" } }
                }
            },
            DeleteAfterImport = false,
            AllowReimportDeleted = false // Default behavior
        };

        var response = await _factory.CreateClient().PostAsJsonAsync("/import", request);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ImportResult>();
        
        result.Should().NotBeNull();
        result!.Results.Single().Ok.Should().BeFalse();

        var dbHash = await db.Hashes.FindAsync(hash.Id);
        await db.Entry(dbHash).ReloadAsync();

        dbHash.Should().NotBeNull();
    }

    [Fact]
    public async Task Import_PreviouslyDeletedImage_ShouldReimportWhenAllowed()
    {
        var db = _factory.Services.CreateScope().ServiceProvider.GetRequiredService<ServerDbContext>();

        var hash = await SetupDeletedImage(db);

        _fileSystem.AddFile("C:/myfile.jpeg", new(TestingConstants.MinimalJpeg));

        var request = new ImportRequest
        {
            Items = new()
            {
                new()
                {
                    Source = new("C:/myfile.jpeg"),
                    Tags = [new() { Namespace = "test", Subtag = "reimport" }]
                }
            },
            DeleteAfterImport = false,
            AllowReimportDeleted = true
        };

        var response = await _factory.CreateClient().PostAsJsonAsync("/import", request);

        response.EnsureSuccessStatusCode();
        
        var result = await response.Content.ReadFromJsonAsync<ImportResult>();
        
        result.Should().NotBeNull();
        result!.Results.Single().Ok.Should().BeTrue();
        
        // Make sure we don't use the one in the change tracker, as that won't reflect the changes from the API.
        var dbHash = await db.Hashes.FindAsync(hash.Id) ?? throw new InvalidOperationException("Should always exist");
        await db.Entry(dbHash).ReloadAsync();
        
        dbHash.DeletedAt.Should().BeNull();
    }

    private async Task<HashItem> SetupDeletedImage(ServerDbContext dbContext)
    {
        var hash = new HashItem
        {
            Hash = new HashedBytes(TestingConstants.MinimalJpeg, ItemType.File).Bytes,
            DeletedAt = DateTime.UtcNow.AddDays(-1)
        };

        dbContext.Hashes.Add(hash);
        await dbContext.SaveChangesAsync();

        return hash;
    }
}