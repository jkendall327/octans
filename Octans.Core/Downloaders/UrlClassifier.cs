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
        
        foreach (var downloader in downloaders)
        {
            var matches = downloader.MatchesUrl(url);

            if (matches)
            {
                return downloader;
            }
        }

        return null;
    }
}