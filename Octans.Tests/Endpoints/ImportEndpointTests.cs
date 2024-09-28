using System.IO.Abstractions.TestingHelpers;
using Octans.Core.Importing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using Octans.Core;
using Xunit.Abstractions;

namespace Octans.Tests;

public class ImportEndpointTests(WebApplicationFactory<Program> factory, ITestOutputHelper helper) : EndpointTest(factory, helper)
{
    [Fact]
    public async Task Import_ValidRequest_ReturnsSuccessResult()
    {
        (var request, var result) = await SendSimpleValidRequest();
        
        result.Should().NotBeNull();
        result.ImportId.Should().Be(request.ImportId);
        result.Results.Single().Ok.Should().BeTrue();
    }
    
    [Fact]
    public async Task Import_ValidRequest_WritesFileToSubfolder()
    {
        _ = await SendSimpleValidRequest();

        var expectedPath = _fileSystem.Path.Join(_appRoot,
            "db",
            "files",
            "f61",
            "61F461B34DCF8D8227A8691A6625444C1E2C793A181C7D0AD5EF8B15D5E6D040.jpg");
        
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

        var hashed = HashedBytes.FromUnhashed(TestingConstants.MinimalJpeg);
        
        mapping.Hash.Hash.Should().BeEquivalentTo(hashed.Bytes, "we should be persisting the hashed bytes");
    }
    
    [Fact]
    public async Task Import_ValidRequest_SendsRequestToThumbnailCreationQueue()
    {
        _ = await SendSimpleValidRequest();
        
        var thumbnailRequest = _spyChannel.WrittenItems.Single();

        thumbnailRequest.Bytes
            .Should()
            .BeEquivalentTo(TestingConstants.MinimalJpeg, "thumbnails should be made for valid imports");
    }
    
    private async Task<(ImportRequest request, ImportResult Content)> SendSimpleValidRequest()
    {
        var mockFile = new MockFileData(TestingConstants.MinimalJpeg);

        var filepath = "C:/image.jpg";
        
        _fileSystem.AddFile(filepath, mockFile);

        var request = BuildRequest(filepath, "category", "example");

        var response = await _api.ProcessImport(request);

        return (request, response.Content!);
    }

    private ImportRequest BuildRequest(string source, string? @namespace, string subtag)
    {
        var tag = new TagModel
        {
            Namespace = @namespace,
            Subtag = subtag
        };

        var item = new ImportItem
        {
            Source = new(source),
            Tags = [tag]
        };
        
        var request = new ImportRequest
        {
            Items = [item],
            DeleteAfterImport = false
        };

        return request;
    }
}