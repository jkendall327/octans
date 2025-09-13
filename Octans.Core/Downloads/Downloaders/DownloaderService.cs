using System.Linq;

namespace Octans.Core.Downloaders;

public class DownloaderService(
    IHttpClientFactory clientFactory,
    DownloaderFactory downloaderFactory)
{
    public async Task<IReadOnlyList<Uri>> ResolveAsync(Uri uri)
    {
        var downloaders = await downloaderFactory.GetDownloaders();

        var matching = downloaders.FirstOrDefault(d => d.MatchesUrl(uri));

        if (matching is null)
        {
            return Array.Empty<Uri>();
        }

#pragma warning disable CA2000
        var client = clientFactory.CreateClient();
#pragma warning restore CA2000

        var raw = await client.GetStringAsync(uri);

        var classification = matching.ClassifyUrl(uri);

        if (classification is DownloaderUrlClassification.Unknown)
        {
            return Array.Empty<Uri>();
        }

        if (classification is DownloaderUrlClassification.Gallery)
        {
            raw = matching.GenerateGalleryHtml(uri.AbsoluteUri, 0);
        }

        var urls = matching
            .ParseHtml(raw)
            .Select(u => new Uri(u))
            .ToList();

        return urls;
    }
}