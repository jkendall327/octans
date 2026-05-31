using System.IO.Abstractions;

namespace Octans.Tests.Infrastructure;

internal sealed class ThrowingMockFileSystem : IFileSystem
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

internal sealed class FakeIoException : Exception
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
