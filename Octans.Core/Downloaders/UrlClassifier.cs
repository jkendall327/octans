namespace Octans.Core.Downloaders;

public class UrlClassifier
{
    private readonly DownloaderFactory _factory;

    public UrlClassifier(DownloaderFactory factory)
    {
        _factory = factory;
    }

    public async Task<Downloader?> Matches(string url)
    {
        var downloaders = await _factory.GetDownloaders();

        return downloaders.FirstOrDefault(d => d.MatchesUrl(url));
    }
}