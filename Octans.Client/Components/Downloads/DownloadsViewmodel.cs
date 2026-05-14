using Octans.Core.Downloads;
using Octans.Core.Downloads.Models;
using Octans.Data.Models;

namespace Octans.Client.Components.Downloads;

public sealed class DownloadsViewmodel(
    IDownloadStateService stateService) : IDisposable
{
    private bool _subscribed;

    public List<DownloadStatusDto> ActiveDownloads { get; private set; } = [];

    public event Func<Task>? StateChanged;

    public async Task InitializeAsync()
    {
        if (!_subscribed)
        {
            stateService.DownloadsChanged += Handle;
            stateService.DownloadStatusChanged += Handle;
            _subscribed = true;
        }

        ActiveDownloads = stateService.GetAllDownloads().Select(MapStatus).ToList();
        await Task.CompletedTask;
    }

    public async ValueTask Handle(DownloadsChanged notification, CancellationToken cancellationToken)
    {
        ActiveDownloads = stateService.GetAllDownloads().Select(MapStatus).ToList();
        var handler = StateChanged;
        if (handler != null)
        {
            await handler();
        }
    }

    public async ValueTask Handle(DownloadStatusChanged notification, CancellationToken cancellationToken)
    {
        var status = MapStatus(notification.Status);
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

    public void Dispose()
    {
        if (!_subscribed) return;

        stateService.DownloadsChanged -= Handle;
        stateService.DownloadStatusChanged -= Handle;
        _subscribed = false;
    }

    private static DownloadStatusDto MapStatus(DownloadStatus status) => new(
        status.Id,
        status.Domain,
        status.Filename,
        status.TotalBytes,
        status.BytesDownloaded,
        status.ProgressPercentage,
        status.CurrentSpeed,
        status.State.ToString());
}

public sealed record DownloadStatusDto(
    Guid Id,
    string Domain,
    string Filename,
    long TotalBytes,
    long BytesDownloaded,
    double ProgressPercentage,
    double CurrentSpeed,
    string State);
