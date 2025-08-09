namespace Octans.Core.Downloaders;

public class DownloaderService(
    IHttpClientFactory clientFactory,
    DownloaderFactory downloaderFactory)
{
    public async Task<byte[]> Download(Uri uri)
    {
        var downloaders = await downloaderFactory.GetDownloaders();

        var matching = downloaders.FirstOrDefault(d => d.MatchesUrl(uri));

        if (matching is null)
        {
            return [];
        }

#pragma warning disable CA2000
        var client = clientFactory.CreateClient();
#pragma warning restore CA2000

        var raw = await client.GetStringAsync(uri);

        var classification = matching.ClassifyUrl(uri);

        if (classification is DownloaderUrlClassification.Unknown)
        {
            return [];
        }

        if (classification is DownloaderUrlClassification.Gallery)
        {
            raw = matching.GenerateGalleryHtml(uri.AbsoluteUri, 0);
        }

        var url = matching.ParseHtml(raw).First();

        return await client.GetByteArrayAsync(new Uri(url));
    }
}