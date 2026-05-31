using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octans.Core.Http;

namespace Octans.Core.Downloaders;

internal sealed class DownloaderService(
    IHttpClientFactory clientFactory,
    IDownloaderFactory downloaderFactory,
    IDownloadRequestHeaderProvider requestHeaderProvider,
    IOptions<DownloaderResolverOptions> options,
    ILogger<DownloaderService> logger)
{
    public async Task<IReadOnlyList<Uri>> ResolveAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        using var operationTimeout = CreateOperationTimeout(cancellationToken);
        var resolverToken = operationTimeout.Token;

        var downloaders = await downloaderFactory.GetDownloaders();

#pragma warning disable CA2000
        var client = clientFactory.CreateClient("DownloadClient");
#pragma warning restore CA2000

        string? raw = null;

        foreach (var downloader in downloaders)
        {
            resolverToken.ThrowIfCancellationRequested();

            if (!TryRun(downloader, "match_url", () => downloader.MatchesUrl(uri, resolverToken)))
            {
                continue;
            }

            var classification = TryRun<DownloaderUrlClassification?>(
                downloader,
                "classify_url",
                () => downloader.ClassifyUrl(uri, resolverToken));
            if (classification is null or DownloaderUrlClassification.Unknown)
            {
                continue;
            }

            if (raw is null)
            {
                raw = await TryRunAsync(downloader, "fetch_html", () => GetStringAsync(client, uri, resolverToken));
                if (raw is null)
                {
                    return [];
                }
            }

            var content = raw;
            if (classification is DownloaderUrlClassification.Gallery)
            {
                var galleryUrl = TryRun(
                    downloader,
                    "generate_url",
                    () => CreateHttpUri(downloader.GenerateGalleryUrl(uri.AbsoluteUri, 0, resolverToken), "generate_url"));

                if (galleryUrl is null)
                {
                    continue;
                }

                content = await TryRunAsync(
                    downloader,
                    "fetch_gallery_html",
                    () => GetStringAsync(client, galleryUrl, resolverToken));
                if (content is null)
                {
                    continue;
                }
            }

            var urls = TryRun(
                downloader,
                "parse_html",
                () => downloader
                    .ParseHtml(content, resolverToken)
                    .Select(u => CreateHttpUri(u, "parse_html"))
                    .ToList());

            if (urls is not null)
            {
                return urls;
            }
        }

        return [];
    }

    private async Task<string> GetStringAsync(HttpClient client, Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        requestHeaderProvider.ApplyHeaders(request);

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadBoundedStringAsync(response, uri, cancellationToken);
    }

    private async Task<string> ReadBoundedStringAsync(
        HttpResponseMessage response,
        Uri uri,
        CancellationToken cancellationToken)
    {
        var maxResponseBytes = options.Value.MaxResponseBytes;
        var reportedBytes = response.Content.Headers.ContentLength;
        if (maxResponseBytes > 0 && reportedBytes > maxResponseBytes)
        {
            throw new DownloaderContractException(
                $"Downloader response from {uri.Host} reported {reportedBytes} bytes, exceeding the configured {maxResponseBytes} byte limit.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var bytes = new byte[81920];
        long totalBytes = 0;

        while (true)
        {
            var bytesRead = await stream.ReadAsync(bytes, cancellationToken);
            if (bytesRead <= 0)
            {
                break;
            }

            if (maxResponseBytes > 0 && totalBytes > maxResponseBytes - bytesRead)
            {
                throw new DownloaderContractException(
                    $"Downloader response from {uri.Host} exceeded the configured {maxResponseBytes} byte limit.");
            }

            await buffer.WriteAsync(bytes.AsMemory(0, bytesRead), cancellationToken);
            totalBytes += bytesRead;
        }

        return GetResponseEncoding(response).GetString(buffer.ToArray());
    }

    private static Encoding GetResponseEncoding(HttpResponseMessage response)
    {
        var charset = response.Content.Headers.ContentType?.CharSet;
        if (string.IsNullOrWhiteSpace(charset))
        {
            return Encoding.UTF8;
        }

        try
        {
            return Encoding.GetEncoding(charset);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }

    private CancellationTokenSource CreateOperationTimeout(CancellationToken cancellationToken)
    {
        var timeout = options.Value.OperationTimeout;
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout > TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            source.CancelAfter(timeout);
        }

        return source;
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
                "Skipping downloader {DownloaderName} after failure during {DownloaderOperation}: {Message}",
                GetDownloaderName(downloader),
                operation,
                ex.Message);
            return default;
        }
    }

    private async Task<T?> TryRunAsync<T>(Downloader downloader, string operation, Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (DownloaderContractException ex)
        {
            logger.LogWarning(
                ex,
                "Skipping downloader {DownloaderName} after failure during {DownloaderOperation}: {Message}",
                GetDownloaderName(downloader),
                operation,
                ex.Message);
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
