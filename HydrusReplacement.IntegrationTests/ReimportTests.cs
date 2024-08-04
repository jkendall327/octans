using System.Net.Http.Json;
using FluentAssertions;
using HydrusReplacement.Core;
using HydrusReplacement.Core.Importing;
using HydrusReplacement.Core.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace HydrusReplacement.IntegrationTests;

public class ReimportTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private ServerDbContext _dbContext;

    public ReimportTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            // Use the same in-memory database setup as in ImportEndpointTests
        });

        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Import_PreviouslyDeletedImage_ShouldNotReimportByDefault()
    {
        // Arrange
        var hash = await SetupDeletedImage();
        
        var request = new ImportRequest
        {
            Items = new List<ImportItem>
            {
                new()
                {
                    // Need to use the mock filesystem to point at a file made with the minimal JPEG.
                    Source = new Uri("https://i.imgur.com/w3fmQPH.jpeg"),
                    Tags = new[] { new TagModel { Namespace = "test", Subtag = "reimport" } }
                }
            },
            DeleteAfterImport = false,
            AllowReimportDeleted = false // Default behavior
        };

        // Act
        var response = await _client.PostAsJsonAsync("/import", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ImportResult>();
        result.Should().NotBeNull();
        result!.Results.Single().Ok.Should().BeFalse();

        var dbHash = await _dbContext.Hashes.FindAsync(hash.Id);
        dbHash.Should().NotBeNull();
    }

    [Fact]
    public async Task Import_PreviouslyDeletedImage_ShouldReimportWhenAllowed()
    {
        // Arrange
        var hash = await SetupDeletedImage();

        var request = new ImportRequest
        {
            Items = new List<ImportItem>
            {
                new()
                {
                    Source = new Uri("http://example.com/image.jpg"),
                    Tags = new[] { new TagModel { Namespace = "test", Subtag = "reimport" } }
                }
            },
            DeleteAfterImport = false,
            AllowReimportDeleted = true
        };

        // Act
        var response = await _client.PostAsJsonAsync("/import", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ImportResult>();
        result.Should().NotBeNull();
        result!.Results.Single().Ok.Should().BeTrue();
        result.Results.Single().Message.Should().Contain("reimported");

        var dbHash = await _dbContext.Hashes.FindAsync(hash.Id);
        dbHash.Should().NotBeNull();
        dbHash.DeletedAt.Should().BeNull();
    }

    private async Task<HashItem> SetupDeletedImage()
    {
        var hash = new HashItem
        {
            Hash = new HashedBytes(TestingConstants.MinimalJpeg, ItemType.File).Bytes,
            DeletedAt = DateTime.UtcNow.AddDays(-1)
        };

        _dbContext.Hashes.Add(hash);
        await _dbContext.SaveChangesAsync();

        return hash;
    }

    public Task InitializeAsync()
    {
        var scope = _factory.Services.CreateScope();
        _dbContext = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _dbContext.Dispose();
        return Task.CompletedTask;
    }
}