using System.IO.Abstractions;
using Microsoft.Extensions.Logging;

namespace Octans.Core.Filesystem;

/// <summary>
/// Shared helpers for writing files through staging paths and managing
/// temporary files that must be cleaned up after use.
/// </summary>
public interface IRobustFileWriter
{
    Task WriteAllBytesAsync(string destinationPath, byte[] bytes, CancellationToken cancellationToken = default);
    RobustTemporaryFile CreateTemporaryFile(string directoryPath, string suggestedFileName);
}

public sealed class RobustFileWriter(
    IFileSystem fileSystem,
    ILogger<RobustFileWriter> logger) : IRobustFileWriter
{
    private const string StagingDirectoryName = ".octans-writes";

    public async Task WriteAllBytesAsync(
        string destinationPath,
        byte[] bytes,
        CancellationToken cancellationToken = default)
    {
        var stagingPath = PrepareStagingPath(destinationPath);

        try
        {
            await using (var stream = fileSystem.File.Create(
                             stagingPath,
                             81920,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            fileSystem.File.Move(stagingPath, destinationPath, true);
        }
        catch
        {
            DeleteFileBestEffort(stagingPath);
            throw;
        }
    }

    public RobustTemporaryFile CreateTemporaryFile(string directoryPath, string suggestedFileName)
    {
        fileSystem.Directory.CreateDirectory(directoryPath);

        var fileName = string.IsNullOrWhiteSpace(suggestedFileName)
            ? "download"
            : suggestedFileName;
        var path = fileSystem.Path.Combine(directoryPath, $"{Guid.NewGuid():N}-{fileName}");

        return new(path, DeleteFileBestEffort);
    }

    private string PrepareStagingPath(string destinationPath)
    {
        var destinationDirectory = GetDestinationDirectory(destinationPath);
        var stagingDirectory = fileSystem.Path.Combine(destinationDirectory, StagingDirectoryName);

        fileSystem.Directory.CreateDirectory(destinationDirectory);
        fileSystem.Directory.CreateDirectory(stagingDirectory);

        var stagingPath = fileSystem.Path.Combine(
            stagingDirectory,
            $"{fileSystem.Path.GetFileName(destinationPath)}.part");

        DeleteFileBestEffort(stagingPath);

        return stagingPath;
    }

    private string GetDestinationDirectory(string destinationPath)
    {
        return fileSystem.Path.GetDirectoryName(destinationPath) ??
               throw new InvalidOperationException("File destination must include a directory.");
    }

    private void DeleteFileBestEffort(string path)
    {
        try
        {
            if (fileSystem.File.Exists(path))
            {
                fileSystem.File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete temporary file {FilePath}", path);
        }
    }
}

public sealed class RobustTemporaryFile(string path, Action<string> cleanup) : IDisposable
{
    private bool _disposed;

    public string Path { get; } = path;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        cleanup(Path);
        _disposed = true;
    }
}
