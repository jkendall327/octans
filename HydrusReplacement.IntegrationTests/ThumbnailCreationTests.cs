using System.IO.Abstractions.TestingHelpers;
using System.Net.Mime;
using System.Threading.Channels;
using FluentAssertions;
using HydrusReplacement.Core;
using HydrusReplacement.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SixLabors.ImageSharp;

namespace HydrusReplacement.IntegrationTests;

public class ThumbnailCreationTests
{
    [Fact]
    public async Task ExecuteAsync_WritesThumbnailToDisk()
    {
        // Arrange
        var mockFileSystem = new MockFileSystem();
        var logger = Substitute.For<ILogger<ThumbnailCreationBackgroundService>>();
        
        // Set up a real SubfolderManager with a mock file system
        var config = new ConfigurationManager();
        
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            {
                "DatabaseRoot", "C:/app"
            }
        });
        
        var subfolderManager = new SubfolderManager(config, mockFileSystem.DirectoryInfo, mockFileSystem.Path);
        
        subfolderManager.MakeSubfolders();
        
        var thumbnailsDirectory = @"C:\thumbnails";
        mockFileSystem.AddDirectory(thumbnailsDirectory);

        // Create a channel and add a test request
        var channel = Channel.CreateUnbounded<ThumbnailCreationRequest>();
        
        var testImageBytes = TestingConstants.MinimalJpeg;
        
        var testRequest = new ThumbnailCreationRequest
        {
            Bytes = testImageBytes,
            Hashed = new(testImageBytes, ItemType.Thumbnail)
        };
        
        await channel.Writer.WriteAsync(testRequest);
        channel.Writer.Complete();

        var service = new ThumbnailCreationBackgroundService(
            channel.Reader,
            subfolderManager,
            mockFileSystem.File,
            logger);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);
        
        var writtenFile = mockFileSystem.AllFiles.Single();
        
        writtenFile.Should().NotBeNull();
        
        var fileContent = await mockFileSystem.File.ReadAllBytesAsync(writtenFile);

        var image = Image.Load(fileContent);

        image.Width.Should().Be(200);
        image.Width.Should().Be(200);
    }
}