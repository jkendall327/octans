using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Octans.Core;
using Octans.Core.Filesystem;
using Octans.Tests.Helpers;

namespace Octans.Tests.Filesystem;

public class ImageStorageTests
{
    private readonly MockFileSystem _fileSystem = new();
    private readonly ImageStorage _sut;

    public ImageStorageTests()
    {
        var options = Substitute.For<IOptions<GlobalSettings>>();
        options.Value.Returns(new GlobalSettings
        {
            AppRoot = "/app"
        });

        _sut = new(options, _fileSystem);
        _sut.EnsureStorage();
    }

    [Fact]
    public void GetOriginalDestination_UsesHashBucketAndMetadataExtension()
    {
        var hash = ContentHash.FromHashBytes([0xde, 0xad]);
        var metadata = new ImageMetadata(".JPEG", "image/jpeg");

        var path = _sut.GetOriginalDestination(hash, metadata);

        path
            .Should()
            .Be("/app/db/files/fde/DEAD.jpeg");
    }

    [Fact]
    public void FindOriginal_UsesDeterministicPathWhenExtensionIsKnown()
    {
        var hash = ContentHash.FromContent(TestingConstants.MinimalJpeg);
        var metadata = _sut.GetMetadata(TestingConstants.MinimalJpeg);
        var path = _sut.GetOriginalDestination(hash, metadata);
        _fileSystem.AddFile(path, new MockFileData(TestingConstants.MinimalJpeg));

        var file = _sut.FindOriginal(hash, metadata.Extension);

        file
            .Should()
            .NotBeNull();

        file!
            .FullName
            .Should()
            .Be(path);
    }

    [Fact]
    public void FindOriginal_FallsBackToBucketSearchWhenExtensionIsUnknown()
    {
        var hash = ContentHash.FromHashBytes([0xde, 0xad]);
        var path = "/app/db/files/fde/DEAD.png";
        _fileSystem.AddFile(path, new MockFileData("content"));

        var file = _sut.FindOriginal(hash);

        file!
            .FullName
            .Should()
            .Be(path);
    }

    [Fact]
    public void DeleteOriginal_RemovesExistingFile()
    {
        var hash = ContentHash.FromHashBytes([0xde, 0xad]);
        var metadata = new ImageMetadata("jpg", "image/jpeg");
        var path = _sut.GetOriginalDestination(hash, metadata);
        _fileSystem.AddFile(path, new MockFileData("content"));

        _sut.DeleteOriginal(hash, metadata.Extension);

        _fileSystem
            .FileExists(path)
            .Should()
            .BeFalse();
    }
}
