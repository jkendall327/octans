using Mediator;
using Octans.Core.Downloaders;

namespace Octans.Core.Downloads;

public class DownloadStatusChanged : INotification
{
    public required DownloadStatus Status { get; init; }
}