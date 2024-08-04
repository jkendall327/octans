using System.IO.Abstractions.TestingHelpers;
using System.Threading.Channels;
using HydrusReplacement.Core;
using HydrusReplacement.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

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

        // Act
        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        // Assert
        var files = mockFileSystem.Directory.GetFiles(thumbnailsDirectory, "*", SearchOption.AllDirectories);
        Assert.Single(files);
        
        var thumbnailPath = files[0];
        Assert.True(mockFileSystem.File.Exists(thumbnailPath));
        
        var fileContent = await mockFileSystem.File.ReadAllBytesAsync(thumbnailPath);
        Assert.NotEmpty(fileContent);
    }
}