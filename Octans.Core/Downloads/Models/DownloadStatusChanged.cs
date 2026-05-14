using Mediator;
using Octans.Data.Models;

namespace Octans.Core.Downloads.Models;

public class DownloadStatusChanged : INotification
{
    public required DownloadStatus Status { get; init; }
}