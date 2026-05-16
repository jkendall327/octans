using System.Globalization;
using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octans.Core.Http.Models;

namespace Octans.Core.Http;

/// <summary>
/// Validates that a destination volume has enough free space for a download.
/// </summary>
public interface IDownloadDiskSpaceGuard
{
    void EnsureSufficientSpace(string destinationPath, long bytesNeeded);
}

/// <summary>
/// Best-effort free-space guard used before and during streamed writes.
/// </summary>
public sealed class DownloadDiskSpaceGuard(
    IFileSystem fileSystem,
    IOptions<DownloadManagerOptions> options,
    ILogger<DownloadDiskSpaceGuard> logger) : IDownloadDiskSpaceGuard
{
    public void EnsureSufficientSpace(string destinationPath, long bytesNeeded)
    {
        if (!options.Value.DiskSpace.Enabled || bytesNeeded <= 0)
        {
            return;
        }

        var driveRoot = fileSystem.Path.GetPathRoot(destinationPath);
        if (string.IsNullOrWhiteSpace(driveRoot))
        {
            logger.LogDebug("Skipping disk-space check because {DestinationPath} has no drive root", destinationPath);
            return;
        }

        try
        {
            var drive = fileSystem.DriveInfo.New(driveRoot);
            if (!drive.IsReady)
            {
                logger.LogDebug("Skipping disk-space check because drive {DriveRoot} is not ready", driveRoot);
                return;
            }

            var headroomBytes = Math.Max(0, options.Value.DiskSpace.RequiredFreeSpaceHeadroomBytes);
            var requiredBytes = AddSaturating(bytesNeeded, headroomBytes);
            var availableBytes = drive.AvailableFreeSpace;
            if (availableBytes >= requiredBytes)
            {
                return;
            }

            throw new DownloadDiskSpaceException(
                "Insufficient free space for download. " +
                $"Need {FormatBytes(requiredBytes)} available " +
                $"({FormatBytes(bytesNeeded)} for content plus {FormatBytes(headroomBytes)} headroom), " +
                $"but only {FormatBytes(availableBytes)} is available on {driveRoot}.");
        }
        catch (DownloadDiskSpaceException)
        {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Could not determine free disk space for {DestinationPath}", destinationPath);
        }
    }

    private static long AddSaturating(long left, long right)
    {
        if (left > long.MaxValue - right)
        {
            return long.MaxValue;
        }

        return left + right;
    }

    private static string FormatBytes(long bytes)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{bytes:N0} bytes");
    }
}

/// <summary>
/// Raised when a download cannot safely continue because the destination volume
/// appears to have insufficient free space.
/// </summary>
public sealed class DownloadDiskSpaceException : IOException
{
    public DownloadDiskSpaceException()
    {
    }

    public DownloadDiskSpaceException(string message) : base(message)
    {
    }

    public DownloadDiskSpaceException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
