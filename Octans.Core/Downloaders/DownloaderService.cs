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
        if (IsRawFile(uri))
        {
            return await DownloadRawFile(uri);
        }

        var downloaders = await _downloaderFactory.GetDownloaders();

        var matching = downloaders.FirstOrDefault(d => d.MatchesUrl(uri.AbsoluteUri));

        if (matching is null)
        {
            return [];
        }

        var client = _clientFactory.CreateClient();

        var raw = await client.GetStringAsync(uri.AbsoluteUri);

        var urls = matching.ParseHtml(raw);
        
        return [];
    }

    private async Task<byte[]> DownloadRawFile(Uri uri)
    {
        var url = uri.AbsoluteUri;
        
        _logger.LogInformation("Downloading remote file from {RemoteUrl}", url);
        
        var client = _clientFactory.CreateClient();

        var bytes = await client.GetByteArrayAsync(url);

        return bytes;
    }

    private bool IsRawFile(Uri uri)
    {
        // Get the last segment of the URI path
        var lastSegment = uri.Segments.Last();

        // Check if the last segment has a file extension
        if (!_fileSystem.Path.HasExtension(lastSegment)) return false;
        
        // List of common raw file extensions
        string[] rawExtensions = [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".txt", ".pdf", ".mp3", ".mp4", ".wav"];

        // Get the extension and convert to lowercase for case-insensitive comparison
        var extension = _fileSystem.Path.GetExtension(lastSegment).ToLowerInvariant();

        // Check if the extension is in the list of raw file extensions
        return rawExtensions.Contains(extension);
    }
}