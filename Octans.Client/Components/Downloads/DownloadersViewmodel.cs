using Octans.Core.Downloaders;

namespace Octans.Client.Components.Downloads;

public class DownloadersViewmodel(DownloaderFactory factory)
{
    public List<DownloaderMetadata> Downloaders { get; private set; } = [];

    public async Task Load()
    {
        var downloaders = await factory.GetDownloaders();
        Downloaders = downloaders.Select(d => d.Metadata).ToList();
    }

    public async Task Rescan()
    {
        var downloaders = await factory.Rescan();
        Downloaders = downloaders.Select(d => d.Metadata).ToList();
    }
}
