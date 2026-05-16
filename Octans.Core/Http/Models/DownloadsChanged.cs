namespace Octans.Core.Http.Models;

/// <summary>
/// Notification that the download list changed.
/// </summary>
public class DownloadsChanged
{
    public Guid? AffectedDownloadId { get; init; }
    public DownloadChangeType ChangeType { get; init; }
}
