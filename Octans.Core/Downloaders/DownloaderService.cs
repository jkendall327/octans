using System.IO.Abstractions;
using Microsoft.Extensions.Logging;

namespace Octans.Core.Downloaders;

public class DownloaderService
{
    private readonly IFileSystem _fileSystem;
    private readonly IHttpClientFactory _clientFactory;
    private readonly DownloaderFactory _downloaderFactory;
    private readonly ILogger<DownloaderService> _logger;

    public DownloaderService(IFileSystem fileSystem,
        IHttpClientFactory clientFactory,
        DownloaderFactory downloaderFactory,
        ILogger<DownloaderService> logger)
    {
        _fileSystem = fileSystem;
        _clientFactory = clientFactory;
        _downloaderFactory = downloaderFactory;
        _logger = logger;
    }

    public async Task<byte[]> Download(Uri uri)
    {
        var downloaders = await _downloaderFactory.GetDownloaders();

        var matching = downloaders.FirstOrDefault(d => d.MatchesUrl(uri.AbsoluteUri));

        if (matching is null)
        {
            return [];
        }

        var client = _clientFactory.CreateClient();

        var raw = await client.GetStringAsync(uri.AbsoluteUri);

        var classification = matching.ClassifyUrl(uri.AbsoluteUri);

        if (classification is DownloaderUrlClassification.Unknown)
        {
            return [];
        }

        if (classification is DownloaderUrlClassification.Gallery)
        {
            raw = matching.GenerateGalleryUrl(uri.AbsoluteUri, 0);
        }
        
        var urls = matching.ParseHtml(raw).First();

        return await client.GetByteArrayAsync(urls);
    }
}