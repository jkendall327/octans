using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Options;
using Octans.Core;
using Octans.Core.Infrastructure;

namespace Octans.Tests.Infrastructure;

public class StorageServiceTests
{
    private readonly MockFileSystem _fileSystem = new();
    private readonly string _testAppRoot = "/app/data";
    private readonly StorageService _storageService;

    public StorageServiceTests()
    {
        var settings = Options.Create(new GlobalSettings
        {
            AppRoot = _testAppRoot
        });

        _storageService = new(_fileSystem, settings);
    }

    [Fact]
    public void GetStorageUsed_EmptyDirectory_ReturnsZeroBytes()
    {
        // Arrange
        _fileSystem.Directory.CreateDirectory(_testAppRoot);

        // Act
        var result = _storageService.GetStorageUsed();

        // Assert
        Assert.Equal("0.0 B", result);
    }

    [Fact]
    public void GetStorageUsed_DirectoryDoesNotExist_ReturnsZeroBytes()
    {
        // Act
        var result = _storageService.GetStorageUsed();

        // Assert
        Assert.Equal("0 B", result);
    }

    [Fact]
    public void GetStorageUsed_SingleFile_ReturnsCorrectSize()
    {
        // Arrange
        _fileSystem.Directory.CreateDirectory(_testAppRoot);
        _fileSystem.File.WriteAllText(
            Path.Combine(_testAppRoot, "test.txt"),
            new('a', 1024)); // 1KB file

        // Act
        var result = _storageService.GetStorageUsed();

        // Assert
        Assert.Equal("1.0 KB", result);
    }

    [Fact]
    public void GetStorageUsed_MultipleFiles_ReturnsCorrectSize()
    {
        // Arrange
        _fileSystem.Directory.CreateDirectory(_testAppRoot);

        // Create 1KB file
        _fileSystem.File.WriteAllText(
            Path.Combine(_testAppRoot, "test1.txt"),
            new('a', 1024));

        // Create 2KB file
        _fileSystem.File.WriteAllText(
            Path.Combine(_testAppRoot, "test2.txt"),
            new('b', 2048));

        // Act
        var result = _storageService.GetStorageUsed();

        // Assert
        Assert.Equal("3.0 KB", result);
    }

    [Fact]
    public void GetStorageUsed_NestedDirectories_ReturnsCorrectSize()
    {
        // Arrange
        _fileSystem.Directory.CreateDirectory(_testAppRoot);
        _fileSystem.Directory.CreateDirectory(Path.Combine(_testAppRoot, "subdir"));

        // Create 1KB file in root
        _fileSystem.File.WriteAllText(
            Path.Combine(_testAppRoot, "test1.txt"),
            new('a', 1024));

        // Create 2KB file in subdirectory
        _fileSystem.File.WriteAllText(
            Path.Combine(_testAppRoot, "subdir", "test2.txt"),
            new('b', 2048));

        // Act
        var result = _storageService.GetStorageUsed();

        // Assert
        Assert.Equal("3.0 KB", result);
    }

    [Fact]
    public void GetStorageUsed_LargeFiles_FormatsCorrectly()
    {
        // Arrange
        _fileSystem.Directory.CreateDirectory(_testAppRoot);

        // Create 1MB file
        var oneMb = 1024 * 1024;
        _fileSystem.AddFile(
            Path.Combine(_testAppRoot, "1mb.bin"),
            new(new byte[oneMb]));

        // Create 5MB file
        var fiveMb = 5 * 1024 * 1024;
        _fileSystem.AddFile(
            Path.Combine(_testAppRoot, "5mb.bin"),
            new(new byte[fiveMb]));

        // Act
        var result = _storageService.GetStorageUsed();

        // Assert
        Assert.Equal("6.0 MB", result);
    }

    [Fact]
    public void GetStorageUsed_ExceptionThrown_ReturnsUnknown()
    {
        var mockFileSystem = new ThrowingMockFileSystem();

        var settings = Options.Create(new GlobalSettings
        {
            AppRoot = _testAppRoot
        });
        var service = new StorageService(mockFileSystem, settings);

        var result = service.GetStorageUsed();

        Assert.Equal("Unknown", result);
    }

    // Helper class that throws exceptions for testing error handling
    private sealed class ThrowingMockFileSystem : IFileSystem
    {
        public IFile File => throw new FakeIoException("Test exception");
        public IDirectory Directory => throw new FakeIoException("Test exception");
        public IFileInfoFactory FileInfo => throw new FakeIoException("Test exception");
        public IFileVersionInfoFactory FileVersionInfo => throw new FakeIoException("Test exception");
        public IFileStreamFactory FileStream => throw new FakeIoException("Test exception");
        public IPath Path => throw new FakeIoException("Test exception");
        public IDirectoryInfoFactory DirectoryInfo => throw new FakeIoException("Test exception");
        public IDriveInfoFactory DriveInfo => throw new FakeIoException("Test exception");
        public IFileSystemWatcherFactory FileSystemWatcher => throw new FakeIoException("Test exception");
    }

    private sealed class FakeIoException : Exception
    {
        public FakeIoException(string message) : base(message)
        {
        }

        public FakeIoException()
        {
        }

        public FakeIoException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
