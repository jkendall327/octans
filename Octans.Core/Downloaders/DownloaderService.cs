using Microsoft.Extensions.Logging;
using Octans.Core.Http;

namespace Octans.Core.Downloaders;

public class DownloaderService(
    IHttpClientFactory clientFactory,
    DownloaderFactory downloaderFactory,
    IDownloadRequestHeaderProvider requestHeaderProvider,
    ILogger<DownloaderService> logger)
{
    public async Task<IReadOnlyList<Uri>> ResolveAsync(Uri uri)
    {
        var downloaders = await downloaderFactory.GetDownloaders();

#pragma warning disable CA2000
        var client = clientFactory.CreateClient("DownloadClient");
#pragma warning restore CA2000

        string? raw = null;

        foreach (var downloader in downloaders)
        {
            if (!TryRun(downloader, "match_url", () => downloader.MatchesUrl(uri)))
            {
                continue;
            }

            var classification = TryRun<DownloaderUrlClassification?>(
                downloader,
                "classify_url",
                () => downloader.ClassifyUrl(uri));
            if (classification is null or DownloaderUrlClassification.Unknown)
            {
                continue;
            }

            var content = raw ??= await GetStringAsync(client, uri);
            if (classification is DownloaderUrlClassification.Gallery)
            {
                var galleryUrl = TryRun(
                    downloader,
                    "generate_url",
                    () => CreateHttpUri(downloader.GenerateGalleryUrl(uri.AbsoluteUri, 0), "generate_url"));

                if (galleryUrl is null)
                {
                    continue;
                }

                content = await GetStringAsync(client, galleryUrl);
            }

            var urls = TryRun(
                downloader,
                "parse_html",
                () => downloader
                    .ParseHtml(content)
                    .Select(u => CreateHttpUri(u, "parse_html"))
                    .ToList());

            if (urls is not null)
            {
                return urls;
            }
        }

        return [];
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

    private T? TryRun<T>(Downloader downloader, string operation, Func<T> action)
    {
        try
        {
            return action();
        }
        catch (DownloaderContractException ex)
        {
            logger.LogWarning(
                ex,
                "Downloader {DownloaderName} failed during {DownloaderOperation}",
                GetDownloaderName(downloader),
                operation);
            return default;
        }
    }

    private static string GetDownloaderName(Downloader downloader)
    {
        return string.IsNullOrWhiteSpace(downloader.Metadata.Name)
            ? "<unnamed>"
            : downloader.Metadata.Name;
    }
}
