using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Octans.Core.Http;

public interface IHttpDocumentFetcher
{
    Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken = default);
}

public sealed class HttpDocumentFetcherOptions
{
    public long MaxResponseBytes { get; set; } = 5L * 1024 * 1024;
}

public sealed class HttpDocumentFetchException : Exception
{
    public HttpDocumentFetchException()
    {
    }

    public HttpDocumentFetchException(string message) : base(message)
    {
    }

    public HttpDocumentFetchException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public static HttpDocumentFetchException ForStatus(Uri uri, HttpStatusCode statusCode)
    {
        return new($"Document request to {uri.Host} failed with HTTP {(int)statusCode} ({statusCode}).");
    }

    public static HttpDocumentFetchException ForReportedSize(Uri uri, long reportedBytes, long maxBytes)
    {
        return new(
            $"Document response from {uri.Host} reported {reportedBytes} bytes, exceeding the configured {maxBytes} byte limit.");
    }

    public static HttpDocumentFetchException ForReceivedSize(Uri uri, long maxBytes)
    {
        return new($"Document response from {uri.Host} exceeded the configured {maxBytes} byte limit.");
    }
}

internal sealed class HttpDocumentFetcher(
    IHttpClientFactory clientFactory,
    IDownloadRequestHeaderProvider requestHeaderProvider,
    IOptions<HttpDocumentFetcherOptions> options,
    ILogger<HttpDocumentFetcher> logger) : IHttpDocumentFetcher
{
    public async Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = clientFactory.CreateClient("DownloadClient");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            requestHeaderProvider.ApplyHeaders(request);

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw HttpDocumentFetchException.ForStatus(uri, response.StatusCode);
            }

            return await ReadBoundedStringAsync(response, uri, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpDocumentFetchException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Document request to {DocumentHost} failed.", uri.Host);
            throw new HttpDocumentFetchException($"Document request to {uri.Host} failed.", ex);
        }
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
            throw HttpDocumentFetchException.ForReportedSize(uri, reportedBytes.Value, maxResponseBytes);
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
                throw HttpDocumentFetchException.ForReceivedSize(uri, maxResponseBytes);
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
}
