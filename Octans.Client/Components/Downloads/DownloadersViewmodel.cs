using Octans.Core.Downloaders;

namespace Octans.Client.Components.Downloads;

public class DownloadersViewmodel
{
    private readonly DownloaderFactory _factory;

    public DownloadersViewmodel(DownloaderFactory factory)
    {
        _factory = factory;
    }

    public List<DownloaderMetadata> Downloaders { get; private set; } = [];

    public async Task Load()
    {
        var downloaders = await _factory.GetDownloaders();
        Downloaders = downloaders.Select(d => d.Metadata).ToList();
    }

    public async Task Rescan()
    {
        var downloaders = await _factory.Rescan();
        Downloaders = downloaders.Select(d => d.Metadata).ToList();
    }
}
