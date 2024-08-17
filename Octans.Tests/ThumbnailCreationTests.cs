using System.IO.Abstractions.TestingHelpers;
using System.Net.Mime;
using System.Threading.Channels;
using FluentAssertions;
using Octans.Core;
using Octans.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SixLabors.ImageSharp;

namespace Octans.Tests;

public class ThumbnailCreationTests
{
    private readonly ILogger<ThumbnailCreationBackgroundService>? _logger = Substitute.For<ILogger<ThumbnailCreationBackgroundService>>();
    private readonly MockFileSystem _mockFileSystem = new();
    private readonly Channel<ThumbnailCreationRequest> _channel = Channel.CreateUnbounded<ThumbnailCreationRequest>();
    private readonly ThumbnailCreationBackgroundService _sut;

    public ThumbnailCreationTests()
    {
        var config = new ConfigurationManager();
        
        config.AddInMemoryCollection(new Dictionary<string, string?> {{"DatabaseRoot", "C:/app"}});
        
        var subfolderManager = new SubfolderManager(config, _mockFileSystem.DirectoryInfo, _mockFileSystem.Path);
        
        subfolderManager.MakeSubfolders();
        
        _sut = new(_channel.Reader, subfolderManager, _mockFileSystem.File, _logger);
    }

    [Fact]
    public async Task ExecuteAsync_WritesThumbnailToDisk()
    {
        await ExecuteRequest(GetMinimalRequest());

        var writtenFile = _mockFileSystem.AllFiles.Single();
        
        writtenFile.Should().NotBeNull();
    }
    
    [Fact]
    public async Task ExecuteAsync_CreatesThumbnailWithCorrectDimensions()
    {
        await ExecuteRequest(GetMinimalRequest());

        var writtenFile = _mockFileSystem.AllFiles.Single();
        
        var fileContent = await _mockFileSystem.File.ReadAllBytesAsync(writtenFile);

        var image = Image.Load(fileContent);

        image.Width.Should().Be(200);
        image.Width.Should().Be(200);
    }

    private async Task ExecuteRequest(ThumbnailCreationRequest request)
    {
        await _channel.Writer.WriteAsync(request);
        _channel.Writer.Complete();

        await _sut.StartAsync(CancellationToken.None);
        await _sut.StopAsync(CancellationToken.None);
    }

    private static ThumbnailCreationRequest GetMinimalRequest()
    {
        var bytes = TestingConstants.MinimalJpeg;

        var request = new ThumbnailCreationRequest
        {
            Bytes = bytes,
            Hashed = new(bytes, ItemType.Thumbnail)
        };
        
        return request;
    }
}