using Mediator;
using Octans.Core.Downloaders;
using Octans.Core.Downloads;

namespace Octans.Client.Downloads;

public sealed class DownloadsViewmodel(
    IDownloadStateService stateService) :
    INotificationHandler<DownloadsChanged>,
    INotificationHandler<DownloadStatusChanged>
{
    public List<DownloadStatus> ActiveDownloads { get; private set; } = [];

    public event Func<Task>? StateChanged;

    public async Task InitializeAsync()
    {
        ActiveDownloads = stateService.GetAllDownloads().ToList();
        await Task.CompletedTask;
    }

    public async ValueTask Handle(DownloadsChanged notification, CancellationToken cancellationToken)
    {
        ActiveDownloads = stateService.GetAllDownloads().ToList();
        var handler = StateChanged;
        if (handler != null)
        {
            await handler();
        }
    }

    public async ValueTask Handle(DownloadStatusChanged notification, CancellationToken cancellationToken)
    {
        var status = notification.Status;
        var index = ActiveDownloads.FindIndex(d => d.Id == status.Id);
        if (index >= 0)
        {
            ActiveDownloads[index] = status;
        }
        else
        {
            ActiveDownloads.Add(status);
        }
        var handler = StateChanged;
        if (handler != null)
        {
            await handler();
        }
    }
}
