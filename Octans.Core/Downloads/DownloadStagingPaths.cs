using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;
using Octans.Data.Models;

namespace Octans.Core.Downloads;

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
        var stagingPath = GetStagingPath(download);
        PrepareFreshStagingPath(download.DestinationPath, stagingPath);
        return stagingPath;
    }

    public string GetSharedStagingPath(QueuedDownload download, string deduplicationKey)
    {
        var destinationDirectory = GetDestinationDirectory(download.DestinationPath);
        var stagingDirectory = fileSystem.Path.Combine(destinationDirectory, StagingDirectoryName);
        var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(deduplicationKey)));
        return fileSystem.Path.Combine(stagingDirectory, $"shared-{keyHash}.part");
    }

    public string PrepareFreshSharedStagingPath(QueuedDownload download, string deduplicationKey)
    {
        var stagingPath = GetSharedStagingPath(download, deduplicationKey);
        PrepareFreshStagingPath(download.DestinationPath, stagingPath);
        return stagingPath;
    }

    private void PrepareFreshStagingPath(string destinationPath, string stagingPath)
    {
        var destinationDirectory = GetDestinationDirectory(destinationPath);
        var stagingDirectory = fileSystem.Path.GetDirectoryName(stagingPath) ??
                               throw new InvalidOperationException("Download staging path must include a directory.");

        fileSystem.Directory.CreateDirectory(destinationDirectory);
        fileSystem.Directory.CreateDirectory(stagingDirectory);

        if (fileSystem.File.Exists(stagingPath))
        {
            fileSystem.File.Delete(stagingPath);
        }
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
