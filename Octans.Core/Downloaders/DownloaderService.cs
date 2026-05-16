using Octans.Core.Http;

namespace Octans.Core.Downloaders;

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
            var galleryUrl = CreateHttpUri(matching.GenerateGalleryUrl(uri.AbsoluteUri, 0), "generate_url");
            raw = await GetStringAsync(client, galleryUrl);
        }

        var urls = matching
            .ParseHtml(raw)
            .Select(u => CreateHttpUri(u, "parse_html"))
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

    private static Uri CreateHttpUri(string value, string source)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new DownloaderContractException($"{source} returned an invalid absolute URL: '{value}'.");
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            throw new DownloaderContractException($"{source} returned a non-HTTP URL: '{value}'.");
        }

        return uri;
    }
}
