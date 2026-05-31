using Octans.Core.Downloaders;

namespace Octans.Client.Components.Downloads;

public class DownloadersViewmodel(IOctansClient client)
{
    public List<DownloaderMetadata> Downloaders { get; private set; } = [];

    public string DownloaderDirectory { get; private set; } = string.Empty;

    public async Task Load()
    {
        Apply(await client.GetDownloadersOverviewAsync());
    }

    public async Task Rescan()
    {
        Apply(await client.RescanDownloadersAsync());
    }

    private void Apply(DownloadersOverviewDto overview)
    {
        DownloaderDirectory = overview.DownloaderDirectory;
        Downloaders = overview.Downloaders.ToList();
    }
}
