using Mediator;

namespace Octans.Core.Downloads;

public class DownloadsChanged : INotification
{
    public Guid? AffectedDownloadId { get; init; }
    public DownloadChangeType ChangeType { get; init; }
}