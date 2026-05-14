using Octans.Data.Models;

namespace Octans.Core.Downloads.Models;

public class DownloadStatusChanged
{
    public required DownloadStatus Status { get; init; }
}
