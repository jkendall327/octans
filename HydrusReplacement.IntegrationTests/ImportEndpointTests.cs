using HydrusReplacement.Core.Importing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using HydrusReplacement.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HydrusReplacement.IntegrationTests;

public class ImportEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public ImportEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<ServerDbContext>));

                services.AddDbContext<ServerDbContext>(options =>
                {
                    options.UseSqlite(_connection);
                });
                
                // Ensure the database is created
                var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
                db.Database.EnsureCreated();
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

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}