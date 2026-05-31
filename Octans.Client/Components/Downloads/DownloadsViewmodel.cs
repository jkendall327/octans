namespace Octans.Client.Components.Downloads;

public sealed class DownloadsViewmodel(
    IOctansClient client) : ViewmodelBase
{
    public List<Octans.Client.DownloadStatusDto> ActiveDownloads { get; private set; } = [];

    public async Task InitializeAsync()
    {
        await RefreshAsync();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        ActiveDownloads = (await client.GetDownloadsAsync(cancellationToken)).ToList();
        await NotifyStateChanged();
    }
}
