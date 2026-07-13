using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octans.Core.Http;

namespace Octans.Core.Downloaders;

public interface IDownloaderDiscoveryService
{
    Task<IReadOnlyList<DownloaderDiscoveryItem>> DiscoverAsync(
        string downloaderName,
        string query,
        int maxItems = 100,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Uri>> ResolveAsync(Uri uri, CancellationToken cancellationToken = default);
}

public sealed record DownloaderDiscoveryItem(string SourceId, Uri RemoteUrl);

internal sealed class DownloaderService(
    IDownloaderFactory downloaderFactory,
    IHttpDocumentFetcher documentFetcher,
    IOptions<DownloaderResolverOptions> options,
    ILogger<DownloaderService> logger) : IDownloaderDiscoveryService
{
    public async Task<IReadOnlyList<DownloaderDiscoveryItem>> DiscoverAsync(
        string downloaderName,
        string query,
        int maxItems = 100,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(downloaderName))
        {
            throw new ArgumentException("Downloader name is required.", nameof(downloaderName));
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Subscription query is required.", nameof(query));
        }

        maxItems = Math.Clamp(maxItems, 1, 10_000);
        using var operationTimeout = CreateOperationTimeout(cancellationToken);
        var resolverToken = operationTimeout.Token;
        var downloader = (await downloaderFactory.GetDownloaders())
            .SingleOrDefault(d => string.Equals(d.Metadata.Name, downloaderName, StringComparison.OrdinalIgnoreCase));

        if (downloader is null)
        {
            throw new InvalidOperationException($"Downloader '{downloaderName}' was not found.");
        }

        var discovered = new List<DownloaderDiscoveryItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var isSeedUrl = Uri.TryCreate(query, UriKind.Absolute, out var seedUri)
                        && seedUri.Scheme is "http" or "https";
        var discoveryQuery = query;
        if (!isSeedUrl && downloader.Metadata.SupportedOperations.Contains("process_query"))
        {
            discoveryQuery = TryRun(
                                downloader,
                                "process_query",
                                () => downloader.ProcessApiQuery(query))
                            ?? query;
        }
        var pageLimit = isSeedUrl ? 1 : Math.Max(1, Math.Min(100, (maxItems + 49) / 50));

        for (var page = 0; page < pageLimit && discovered.Count < maxItems; page++)
        {
            resolverToken.ThrowIfCancellationRequested();
            var galleryUri = isSeedUrl
                ? seedUri!
                : CreateHttpUri(
                    TryRun(downloader, "generate_url", () => downloader.GenerateGalleryUrl(discoveryQuery, page, resolverToken))
                        ?? throw new DownloaderContractException("generate_url failed."),
                    "generate_url");
            var html = await documentFetcher.GetStringAsync(galleryUri, resolverToken);
            var candidates = TryRun(
                downloader,
                "parse_html",
                () => downloader.ParseHtml(html, resolverToken)
                    .Select(url => CreateHttpUri(url, "parse_html"))
                    .ToList()) ?? [];

            if (candidates.Count == 0)
            {
                break;
            }

            var newOnPage = 0;
            foreach (var candidate in candidates)
            {
                resolverToken.ThrowIfCancellationRequested();
                var candidateId = NormalizeSourceId(candidate);
                var classification = TryRun(
                    downloader,
                    "classify_url",
                    () => downloader.ClassifyUrl(candidate, resolverToken));

                var mediaUrls = classification is DownloaderUrlClassification.Post
                    ? await ResolveWithDownloaderAsync(downloader, candidate, resolverToken)
                    : [candidate];

                foreach (var mediaUrl in mediaUrls)
                {
                    var key = $"{candidateId}|{NormalizeSourceId(mediaUrl)}";
                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    discovered.Add(new(candidateId, mediaUrl));
                    newOnPage++;
                    if (discovered.Count >= maxItems)
                    {
                        break;
                    }
                }

                if (discovered.Count >= maxItems)
                {
                    break;
                }
            }

            if (isSeedUrl || newOnPage == 0)
            {
                break;
            }
        }

        return discovered;
    }

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

            var urls = classification is DownloaderUrlClassification.Gallery
                ? await ResolveGalleryAsync(downloader, uri, resolverToken)
                : TryRun(
                    downloader,
                    "parse_html",
                    () => downloader
                        .ParseHtml(raw, resolverToken)
                        .Select(u => CreateHttpUri(u, "parse_html"))
                        .ToList());

            if (urls is not null)
            {
                return urls;
            }
        }

        return [];
    }

    private async Task<IReadOnlyList<Uri>> ResolveWithDownloaderAsync(
        Downloader downloader,
        Uri uri,
        CancellationToken cancellationToken)
    {
        var raw = await documentFetcher.GetStringAsync(uri, cancellationToken);
        return TryRun(
                downloader,
                "parse_html",
                () => downloader
                    .ParseHtml(raw, cancellationToken)
                    .Select(u => CreateHttpUri(u, "parse_html"))
                    .ToList())
            ?? [];
    }

    private async Task<IReadOnlyList<Uri>> ResolveGalleryAsync(
        Downloader downloader,
        Uri uri,
        CancellationToken cancellationToken)
    {
        var galleryUrl = TryRun(
            downloader,
            "generate_url",
            () => CreateHttpUri(downloader.GenerateGalleryUrl(uri.AbsoluteUri, 0, cancellationToken), "generate_url"));
        if (galleryUrl is null)
        {
            return [];
        }

        var content = await TryRunAsync(
            downloader,
            "fetch_gallery_html",
            () => documentFetcher.GetStringAsync(galleryUrl, cancellationToken));
        if (content is null)
        {
            return [];
        }

        return TryRun(
                downloader,
                "parse_html",
                () => downloader
                    .ParseHtml(content, cancellationToken)
                    .Select(u => CreateHttpUri(u, "parse_html"))
                    .ToList())
            ?? [];
    }

    private static string NormalizeSourceId(Uri uri) => uri.GetComponents(
        UriComponents.SchemeAndServer | UriComponents.PathAndQuery,
        UriFormat.UriEscaped).TrimEnd('/');

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
