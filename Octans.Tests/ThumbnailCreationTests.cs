using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using Octans.Core;
using Octans.Server;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SixLabors.ImageSharp;

namespace Octans.Tests;

public class ThumbnailCreationTests
{
    private readonly MockFileSystem _mockFileSystem = new();
    private readonly ThumbnailCreator _sut;

    public ThumbnailCreationTests()
    {
        var options = Substitute.For<IOptions<GlobalSettings>>();

        options.Value.Returns(new GlobalSettings
        {
            AppRoot = "C:/app"
        });

        var subfolderManager = new SubfolderManager(options, _mockFileSystem);
        subfolderManager.MakeSubfolders();

        _sut = new(_mockFileSystem, options, NullLogger<ThumbnailCreator>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_WritesThumbnailToDisk()
    {
        await _sut.ProcessThumbnailRequestAsync(GetMinimalRequest());

        var writtenFile = _mockFileSystem.AllFiles.Single();

        writtenFile.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_CreatesThumbnailWithCorrectDimensions()
    {
        await _sut.ProcessThumbnailRequestAsync(GetMinimalRequest());

        var writtenFile = _mockFileSystem.AllFiles.Single();

        var fileContent = await _mockFileSystem.File.ReadAllBytesAsync(writtenFile);

        var image = Image.Load(fileContent);

        image.Width.Should().Be(200);
        image.Width.Should().Be(200);
    }

    private static ThumbnailCreationRequest GetMinimalRequest()
    {
        var bytes = TestingConstants.MinimalJpeg;

        var request = new ThumbnailCreationRequest
        {
            Bytes = bytes,
            Hashed = HashedBytes.FromUnhashed(bytes)
        };

        return request;
    }
}