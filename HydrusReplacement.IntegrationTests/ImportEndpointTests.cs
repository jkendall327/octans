using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using HydrusReplacement.Core.Importing;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Json;
using HydrusReplacement.Core;

namespace HydrusReplacement.IntegrationTests;

public class ImportEndpointTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly IContainer _dbContainer;
    private HttpClient _client;

    public ImportEndpointTests()
    {
        _factory = new();
        
        _dbContainer = new ContainerBuilder()
            .WithImage("sqlite:latest")
            .WithPortBinding(5432, true)
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();

        _client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Configure your services to use the test container
                // This might involve replacing the DbContext configuration
            });
        }).CreateClient();
    }

    public async Task DisposeAsync()
    {
        await _dbContainer.StopAsync();
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Import_ValidRequest_ReturnsSuccessResult()
    {
        // Arrange
        var request = new ImportRequest
        {
            Items = new()
            {
                new()
                {
                    Source = new("https://example.com/image.jpg"),
                    Tags = new[]
                    {
                        new TagModel() { Namespace = "category", Subtag = "example" }
                    }
                }
            },
            DeleteAfterImport = false
        };

        // Act
        var response = await _client.PostAsJsonAsync("/import", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ImportResult>();
        Assert.NotNull(result);
        Assert.Equal(request.ImportId, result.ImportId);
        Assert.Single(result.Results);
        Assert.True(result.Results[0].Ok);
    }

    [Fact]
    public async Task Import_InvalidRequest_ReturnsBadRequest()
    {
        // Arrange
        var request = new ImportRequest
        {
            Items = new(), // Empty list, should be invalid
            DeleteAfterImport = false
        };

        // Act
        var response = await _client.PostAsJsonAsync("/import", request);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    // Add more tests as needed
}