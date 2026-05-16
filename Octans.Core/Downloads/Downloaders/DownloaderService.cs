using Octans.Core.Downloads;

namespace Octans.Core.Downloads.Downloaders;

public class DownloaderService(
    IHttpClientFactory clientFactory,
    DownloaderFactory downloaderFactory,
    IDownloadRequestHeaderProvider requestHeaderProvider)
{
    public async Task<IReadOnlyList<Uri>> ResolveAsync(Uri uri)
    {
        var downloaders = await downloaderFactory.GetDownloaders();

        var matching = downloaders.FirstOrDefault(d => d.MatchesUrl(uri));

        if (matching is null)
        {
            return [];
        }

#pragma warning disable CA2000
        var client = clientFactory.CreateClient("DownloadClient");
#pragma warning restore CA2000

        var raw = await GetStringAsync(client, uri);

        var classification = matching.ClassifyUrl(uri);

        if (classification is DownloaderUrlClassification.Unknown)
        {
            return [];
        }

        if (classification is DownloaderUrlClassification.Gallery)
        {
            var galleryUrl = matching.GenerateGalleryUrl(uri.AbsoluteUri, 0);
            raw = await GetStringAsync(client, new Uri(galleryUrl));
        }

        var urls = matching
            .ParseHtml(raw)
            .Select(u => new Uri(u))
            .ToList();

        return urls;
    }

    private async Task<string> GetStringAsync(HttpClient client, Uri uri)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        requestHeaderProvider.ApplyHeaders(request);

        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
}
