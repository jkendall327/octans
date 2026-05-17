using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octans.Core.Filesystem;

namespace Octans.Tests.Filesystem;

public sealed class RobustFileWriterTests
{
    private readonly MockFileSystem _fileSystem = new();
    private readonly RobustFileWriter _sut;

    public RobustFileWriterTests()
    {
        _sut = new(_fileSystem, NullLogger<RobustFileWriter>.Instance);
    }

    [Fact]
    public async Task WriteAllBytesAsync_WritesThroughStagingFileAndRemovesStalePart()
    {
        var destination = "/library/originals/image.jpg";
        var stagingPath = "/library/originals/.octans-writes/image.jpg.part";
        var bytes = "final"u8.ToArray();

        _fileSystem.Directory.CreateDirectory(_fileSystem.Path.GetDirectoryName(stagingPath)!);
        await _fileSystem.File.WriteAllTextAsync(stagingPath, "stale");

        await _sut.WriteAllBytesAsync(destination, bytes);

        (await _fileSystem.File.ReadAllBytesAsync(destination)).Should().BeEquivalentTo(bytes);
        _fileSystem.File.Exists(stagingPath).Should().BeFalse();
    }

    [Fact]
    public async Task CreateTemporaryFile_DisposeDeletesFileBestEffort()
    {
        var temp = _sut.CreateTemporaryFile("/tmp/octans-imports", "image.jpg");

        await _fileSystem.File.WriteAllTextAsync(temp.Path, "downloaded");

        temp.Dispose();

        _fileSystem.File.Exists(temp.Path).Should().BeFalse();
    }
}
