using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Options;
using Octans.Core;
using Octans.Server.Services;
using Xunit.Abstractions;

namespace Octans.Tests.Infrastructure;

public class StorageServiceTests
{
    private readonly ITestOutputHelper _output;
    private readonly MockFileSystem _fileSystem;
    private readonly string _testAppRoot;
    private readonly StorageService _storageService;

    public StorageServiceTests(ITestOutputHelper output)
    {
        _output = output;
        _fileSystem = new MockFileSystem();
        _testAppRoot = "/app/data";

        var settings = Options.Create(new GlobalSettings
        {
            AppRoot = _testAppRoot
        });

        _storageService = new StorageService(_fileSystem, settings);
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
            new string('a', 1024)); // 1KB file

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
            new string('a', 1024));
        
        // Create 2KB file
        _fileSystem.File.WriteAllText(
            Path.Combine(_testAppRoot, "test2.txt"), 
            new string('b', 2048));

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
            new string('a', 1024));
        
        // Create 2KB file in subdirectory
        _fileSystem.File.WriteAllText(
            Path.Combine(_testAppRoot, "subdir", "test2.txt"), 
            new string('b', 2048));

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
        var oneMB = 1024 * 1024;
        _fileSystem.AddFile(
            Path.Combine(_testAppRoot, "1mb.bin"), 
            new MockFileData(new byte[oneMB]));
        
        // Create 5MB file
        var fiveMB = 5 * 1024 * 1024;
        _fileSystem.AddFile(
            Path.Combine(_testAppRoot, "5mb.bin"), 
            new MockFileData(new byte[fiveMB]));

        // Act
        var result = _storageService.GetStorageUsed();

        // Assert
        Assert.Equal("6.0 MB", result);
    }

    [Fact]
    public void GetStorageUsed_ExceptionThrown_ReturnsUnknown()
    {
        // Arrange
        var mockFileSystem = new ThrowingMockFileSystem();
        var settings = Options.Create(new GlobalSettings
        {
            AppRoot = _testAppRoot
        });
        var service = new StorageService(mockFileSystem, settings);

        // Act
        var result = service.GetStorageUsed();

        // Assert
        Assert.Equal("Unknown", result);
    }

    // Helper class that throws exceptions for testing error handling
    private class ThrowingMockFileSystem : IFileSystem
    {
        public IFile File => throw new Exception("Test exception");
        public IDirectory Directory => throw new Exception("Test exception");
        public IFileInfoFactory FileInfo => throw new Exception("Test exception");
        public IFileVersionInfoFactory FileVersionInfo => throw new Exception("Test exception");
        public IFileStreamFactory FileStream => throw new Exception("Test exception");
        public IPath Path => throw new Exception("Test exception");
        public IDirectoryInfoFactory DirectoryInfo => throw new Exception("Test exception");
        public IDriveInfoFactory DriveInfo => throw new Exception("Test exception");
        public IFileSystemWatcherFactory FileSystemWatcher => throw new Exception("Test exception");
    }
}
