using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octans.Core.Http;

namespace Octans.Core.Downloaders;

internal sealed class DownloaderService(
    IDownloaderFactory downloaderFactory,
    IHttpDocumentFetcher documentFetcher,
    IOptions<DownloaderResolverOptions> options,
    ILogger<DownloaderService> logger)
{
    public async Task<IReadOnlyList<Uri>> ResolveAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        using var operationTimeout = CreateOperationTimeout(cancellationToken);
        var resolverToken = operationTimeout.Token;

        var downloaders = await downloaderFactory.GetDownloaders();

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
                raw = await TryRunAsync(downloader, "fetch_html", () => documentFetcher.GetStringAsync(uri, resolverToken));
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
                    () => documentFetcher.GetStringAsync(galleryUrl, resolverToken));
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
        catch (HttpDocumentFetchException ex)
        {
            logger.LogWarning(
                ex,
                "Skipping downloader {DownloaderName} after failure during {DownloaderOperation}: {Message}",
                GetDownloaderName(downloader),
                operation,
                ex.Message);
            return default;
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
