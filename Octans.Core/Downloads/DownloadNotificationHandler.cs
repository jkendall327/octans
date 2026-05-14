using Mediator;
using Octans.Core.Downloads.Models;

namespace Octans.Core.Downloads;

public sealed class DownloadNotificationHandler :
    INotificationHandler<DownloadsChanged>,
    INotificationHandler<DownloadStatusChanged>
{
    public ValueTask Handle(DownloadsChanged notification, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask Handle(DownloadStatusChanged notification, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
