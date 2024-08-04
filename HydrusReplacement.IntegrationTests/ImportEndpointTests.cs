using System.Net;
using HydrusReplacement.Core.Importing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using FluentAssertions;
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

        var response = await client.PostAsJsonAsync("/import", request);

        response.EnsureSuccessStatusCode();
        
        var result = await response.Content.ReadFromJsonAsync<ImportResult>();

        result.Should().NotBeNull();
        result!.ImportId.Should().Be(request.ImportId);
        result.Results.Should().ContainSingle();
        result.Results[0].Ok.Should().BeTrue();
    }

    [Fact]
    public async Task Import_InvalidRequest_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var request = new ImportRequest
        {
            Items = [], // Empty list, should be invalid
            DeleteAfterImport = false
        };

        var response = await client.PostAsJsonAsync("/import", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    public async Task InitializeAsync() => await _connection.OpenAsync();
    public async Task DisposeAsync() => await _connection.DisposeAsync();
}