using Octans.Data.Models;

namespace Octans.Core.Downloads.Models;

/// <summary>
/// Notification that a single download status changed.
/// </summary>
public class DownloadStatusChanged
{
    public required DownloadStatus Status { get; init; }
}
