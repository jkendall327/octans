using System.IO.Abstractions;
using Octans.Data.Models;

namespace Octans.Core.Downloads;

/// <summary>
/// Builds and manages temporary staging paths for in-progress HTTP downloads.
/// </summary>
public sealed class DownloadStagingPaths(IFileSystem fileSystem)
{
    private const string StagingDirectoryName = ".octans-downloads";

    public string GetStagingPath(QueuedDownload download)
    {
        return GetStagingPath(download.Id, download.DestinationPath);
    }

    public string GetStagingPath(Guid downloadId, string destinationPath)
    {
        var destinationDirectory = GetDestinationDirectory(destinationPath);
        var stagingDirectory = fileSystem.Path.Combine(destinationDirectory, StagingDirectoryName);

        return fileSystem.Path.Combine(stagingDirectory, $"{downloadId}.part");
    }

    public string PrepareFreshStagingPath(QueuedDownload download)
    {
        var destinationDirectory = GetDestinationDirectory(download.DestinationPath);
        var stagingDirectory = fileSystem.Path.Combine(destinationDirectory, StagingDirectoryName);

        fileSystem.Directory.CreateDirectory(destinationDirectory);
        fileSystem.Directory.CreateDirectory(stagingDirectory);

        var stagingPath = GetStagingPath(download);
        if (fileSystem.File.Exists(stagingPath))
        {
            fileSystem.File.Delete(stagingPath);
        }

        return stagingPath;
    }

    public void DeleteStagingFile(Guid downloadId, string destinationPath)
    {
        var stagingPath = GetStagingPath(downloadId, destinationPath);
        if (fileSystem.File.Exists(stagingPath))
        {
            fileSystem.File.Delete(stagingPath);
        }
    }

    public void MoveToDestination(QueuedDownload download, string stagingPath)
    {
        fileSystem.File.Move(stagingPath, download.DestinationPath, true);
    }

    private string GetDestinationDirectory(string destinationPath)
    {
        return fileSystem.Path.GetDirectoryName(destinationPath) ??
               throw new InvalidOperationException("Download destination must include a directory.");
    }
}
