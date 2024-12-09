using Octans.Client;
using Octans.Core.Importing;
using System.IO.Abstractions.TestingHelpers;
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace Octans.Tests;

public class ImportRequestSenderTests
{
    private readonly MockFileSystem _mockFileSystem = new();
    private readonly ImportRequestSender _sut = null!;
    private readonly IHttpClientFactory _factory = Substitute.For<IHttpClientFactory>();

    public ImportRequestSenderTests()
    {
        var environment = Substitute.For<IWebHostEnvironment>();
        var path = _mockFileSystem.Path.Join("C:", "fakepath");
        environment.WebRootPath.Returns(path);
        
        //_sut = new(_mockFileSystem, environment, new ServerClient());
    }

    [Fact (Skip = "Work in progress")]
    public async Task SendImportRequest_WithValidUrls_ReturnsSuccessResults()
    {
        var importUrls = "http://example.com/file1\nhttp://example.com/file2";

        var response = GetSuccessResponse();
        SetupClient(HttpStatusCode.OK, response);

        var result = await _sut.SendImportRequest(importUrls, []);

        result.Should().HaveCount(2);
        result.Should().AllBe("Success");
    }

    [Fact (Skip = "Work in progress")]
    public async Task SendImportRequest_WithValidFiles_ReturnsSuccessResults()
    {
        // Arrange
        var files = new List<IFormFile>
        {
            CreateMockFormFile("file1.txt", "content1"),
            CreateMockFormFile("file2.txt", "content2")
        };

        var response = GetSuccessResponse();
        SetupClient(HttpStatusCode.OK, response);
        
        // Act
        var result = await _sut.SendImportRequest(string.Empty, files);

        // Assert
        result.Should().HaveCount(2);
        result.Should().AllBe("Success");

        var uploads = _mockFileSystem.Path.Join("C:", "fakepath", "uploads");
        
        _mockFileSystem.FileExists(_mockFileSystem.Path.Join(uploads, "file1.txt")).Should().BeTrue();
        _mockFileSystem.FileExists(_mockFileSystem.Path.Join(uploads, "file2.txt")).Should().BeTrue();
    }

    [Fact (Skip = "Work in progress")]
    public async Task SendImportRequest_WithNoItemsToImport_ReturnsAppropriateMessage()
    {
        var result = await _sut.SendImportRequest(string.Empty, []);

        result.Should().HaveCount(1);
        result.Should().Contain("Nothing to import.");
    }

    [Fact (Skip = "Work in progress")]
    public async Task SendImportRequest_WithHttpFailure_ReturnsErrorMessage()
    {
        var importUrls = "http://example.com/file1";
        
        SetupClient(HttpStatusCode.InternalServerError, null);

        var result = await _sut.SendImportRequest(importUrls, []);

        result.Should().HaveCount(1);
        result.Should().Contain("Failed to process import request.");
    }
    
    private static ImportResult GetSuccessResponse()
    {
        List<ImportItemResult> results = [new() { Ok = true }, new() { Ok = true }];
        return new(Guid.NewGuid(), results);
    }

    private static IFormFile CreateMockFormFile(string fileName, string content)
    {
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        return new FormFile(stream, 0, stream.Length, "files", fileName);
    }
    
    private void SetupClient(HttpStatusCode code, object? content)
    {
        var client = new HttpClient(new FakeHttpMessageHandler(code, content));
        client.BaseAddress = new("http://foobar.com/");
        
        _factory.CreateClient("ServerApi").Returns(client);
    }
}