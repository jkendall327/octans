using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Net;
using HydrusReplacement.Core.Importing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using FluentAssertions;
using HydrusReplacement.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HydrusReplacement.IntegrationTests;

public class ImportEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly MockFileSystem _fileSystem = new();
    
    private readonly byte[] _minimalJpeg =
    [
        0xFF, 0xD8,             // SOI marker
        0xFF, 0xE0,             // APP0 marker
        0x00, 0x10,             // Length of APP0 segment
        0x4A, 0x46, 0x49, 0x46, 0x00, // JFIF identifier
        0x01, 0x01,             // JFIF version
        0x00,                   // Units
        0x00, 0x01,             // X density
        0x00, 0x01,             // Y density
        0x00, 0x00,             // Thumbnail width and height
        0xFF, 0xD9              // EOI marker
    ];

    private readonly string _appRoot = "C:/app";

    public ImportEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(IPath));
                services.RemoveAll(typeof(IDirectoryInfo));
                services.RemoveAll(typeof(IFile));

                services.AddSingleton(_fileSystem.Path);
                services.AddSingleton(_fileSystem.DirectoryInfo);
                services.AddSingleton(_fileSystem.File);
                
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
            
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    {
                        "DatabaseRoot", _appRoot
                    }
                });
            });
        });
    }
    
    [Fact]
    public async Task Import_ValidRequest_ReturnsSuccessResult()
    {
        var client = _factory.CreateClient();
        
        var mockFile = new MockFileData(_minimalJpeg);

        var filepath = "C:/image.jpg";
        
        _fileSystem.AddFile(filepath, mockFile);

        var request = BuildRequest(filepath, "category", "example");

        var response = await client.PostAsJsonAsync("/import", request);

        response.EnsureSuccessStatusCode();
        
        var result = await response.Content.ReadFromJsonAsync<ImportResult>();

        result.Should().NotBeNull();
        result!.ImportId.Should().Be(request.ImportId);
        result.Results.Single().Ok.Should().BeTrue();
    }
    
    [Fact]
    public async Task Import_ValidRequest_WritesFileToSubfolder()
    {
        var client = _factory.CreateClient();
     
        var mockFile = new MockFileData(_minimalJpeg);

        var filepath = "C:/image.jpg";
        
        _fileSystem.AddFile(filepath, mockFile);

        var request = BuildRequest(filepath, "category", "example");

        var response = await client.PostAsJsonAsync("/import", request);

        response.EnsureSuccessStatusCode();
        
        _ = await response.Content.ReadFromJsonAsync<ImportResult>();

        var expectedPath = _fileSystem.Path.Join(_appRoot, "db", "files", "fd2", "D20F6FFD523B78A86CD2F916FA34AF5D1918D75F7B142237C752AD6B254213AB.jpg");
        
        var file = _fileSystem.GetFile(expectedPath);

        file.Should().NotBeNull();
    }
    
    [Fact]
    public async Task Import_ValidRequest_PersistsInfoToDatabase()
    {
        var client = _factory.CreateClient();
        
        var mockFile = new MockFileData(_minimalJpeg);

        var filepath = "C:/image.jpg";
        
        _fileSystem.AddFile(filepath, mockFile);

        var request = BuildRequest(filepath, "category", "example");

        var response = await client.PostAsJsonAsync("/import", request);

        response.EnsureSuccessStatusCode();
        
        _ = await response.Content.ReadFromJsonAsync<ImportResult>();

        var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();

        var tag = db.Tags
            .Include(tag => tag.Namespace)
            .Include(tag => tag.Subtag)
            .Single();

        var @namespace = tag.Namespace;
        var subtag = tag.Subtag;

        @namespace.Value.Should().Be("category");
        subtag.Value.Should().Be("example");

        var mapping = await db.Mappings
            .Include(mapping => mapping.Tag)
            .Include(mapping => mapping.Hash)
            .SingleAsync();

        mapping.Tag.Should().Be(tag);

        var hash = db.Hashes.Single();

        mapping.Hash.Should().Be(hash);
    }

    private ImportRequest BuildRequest(string source, string? @namespace, string subtag)
    {
        var request = new ImportRequest
        {
            Items =
            [
                new()
                {
                    Source = new(source),
                    Tags =
                    [
                        new()
                        {
                            Namespace = @namespace,
                            Subtag = subtag
                        }
                    ]
                }
            ],
            
            DeleteAfterImport = false
        };

        return request;
    }

    public async Task InitializeAsync() => await _connection.OpenAsync();
    public async Task DisposeAsync() => await _connection.DisposeAsync();
}