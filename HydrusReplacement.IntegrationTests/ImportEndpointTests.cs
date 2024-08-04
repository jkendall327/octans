using HydrusReplacement.Core.Importing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using HydrusReplacement.Core.Models;

namespace HydrusReplacement.IntegrationTests;

public class ImportEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ImportEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<ServerDbContext>));

                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<ServerDbContext>(options =>
                {
                    options.UseInMemoryDatabase("TestDatabase");
                });
            });
        });
    }

    [Fact]
    public async Task Import_ValidRequest_ReturnsSuccessResult()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new ImportRequest
        {
            Items =
            [
                new()
                {
                    Source = new("https://example.com/image.jpg"),
                    Tags =
                    [
                        new()
                        {
                            Namespace = "category",
                            Subtag = "example"
                        }
                    ]
                }
            ],
            DeleteAfterImport = false
        };

        // Act
        var response = await client.PostAsJsonAsync("/import", request);

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
        var client = _factory.CreateClient();
        var request = new ImportRequest
        {
            Items = [], // Empty list, should be invalid
            DeleteAfterImport = false
        };

        // Act
        var response = await client.PostAsJsonAsync("/import", request);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }
}