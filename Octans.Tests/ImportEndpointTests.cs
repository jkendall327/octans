using System.IO.Abstractions.TestingHelpers;
using Octans.Core.Importing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using FluentAssertions;
using Octans.Core;

namespace Octans.Tests;

public class ImportEndpointTests : EndpointTest
{
    public ImportEndpointTests(WebApplicationFactory<Program> factory) : base(factory)
    {
    }
    
    [Fact]
    public async Task Import_ValidRequest_ReturnsSuccessResult()
    {
        (var request, var response) = await SendSimpleValidRequest();
        
        var result = await response.Content.ReadFromJsonAsync<ImportResult>();

        result.Should().NotBeNull();
        result!.ImportId.Should().Be(request.ImportId);
        result.Results.Single().Ok.Should().BeTrue();
    }
    
    [Fact]
    public async Task Import_ValidRequest_WritesFileToSubfolder()
    {
        _ = await SendSimpleValidRequest();

        var expectedPath = _fileSystem.Path.Join(_appRoot, "db", "files", "f61", "61F461B34DCF8D8227A8691A6625444C1E2C793A181C7D0AD5EF8B15D5E6D040.jpg");
        
        var file = _fileSystem.GetFile(expectedPath);

        file.Should().NotBeNull();
    }
    
    [Fact]
    public async Task Import_ValidRequest_PersistsInfoToDatabase()
    {
        _ = await SendSimpleValidRequest();
        
        var mapping = _context.Mappings
            .Include(mapping => mapping.Hash)
            .Include(mapping => mapping.Tag)
            .ThenInclude(tag => tag.Namespace)
            .Include(mapping => mapping.Tag)
            .ThenInclude(tag => tag.Subtag)
            .Single();

        mapping.Tag.Namespace.Value.Should().Be("category", "the namespace should be linked the tag");
        mapping.Tag.Subtag.Value.Should().Be("example", "the subtag should be linked to the tag");

        var hashed = new HashedBytes(TestingConstants.MinimalJpeg, ItemType.File);
        
        mapping.Hash.Hash.Should().BeEquivalentTo(hashed.Bytes, "we should be persisting the hashed bytes");
    }
    
    [Fact]
    public async Task Import_ValidRequest_SendsRequestToThumbnailCreationQueue()
    {
        _ = await SendSimpleValidRequest();
        
        var thumbnailRequest = _spyChannel.WrittenItems.Single();

        thumbnailRequest.Bytes.Should().BeEquivalentTo(TestingConstants.MinimalJpeg, "thumbnails should be made for valid imports");
    }
    
    private async Task<(ImportRequest request, HttpResponseMessage response)> SendSimpleValidRequest()
    {
        var client = _factory.CreateClient();
        
        var mockFile = new MockFileData(TestingConstants.MinimalJpeg);

        var filepath = "C:/image.jpg";
        
        _fileSystem.AddFile(filepath, mockFile);

        var request = BuildRequest(filepath, "category", "example");

        var response = await client.PostAsJsonAsync("/import", request);

        response.EnsureSuccessStatusCode();

        return (request, response);
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
}