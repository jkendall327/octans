using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Octans.Core.Downloads.Models;

namespace Octans.Core.Downloads;

public interface IDownloadRequestHeaderProvider
{
    DownloadRequestHeaders GetHeaders(Uri uri);
    string GetRequestFingerprint(Uri uri);
    void ApplyHeaders(HttpRequestMessage request);
}

public sealed class DownloadRequestHeaderProvider(IOptions<DownloadManagerOptions> options) : IDownloadRequestHeaderProvider
{
    private static readonly HashSet<string> SensitiveHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Cookie",
        "Proxy-Authorization",
        "Set-Cookie"
    };

    public DownloadRequestHeaders GetHeaders(Uri uri)
    {
        var headerOptions = options.Value.RequestHeaders;
        var domain = GetMatchingDomainOptions(uri.Host, headerOptions.Domains);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        AddHeader(headers, "User-Agent", domain?.UserAgent ?? headerOptions.DefaultUserAgent);
        AddHeader(headers, "Authorization", domain?.Authorization);
        AddHeader(headers, "Cookie", domain?.Cookie);

        foreach (var (name, value) in headerOptions.Headers)
        {
            AddHeader(headers, name, value);
        }

        if (domain is not null)
        {
            foreach (var (name, value) in domain.Headers)
            {
                AddHeader(headers, name, value);
            }
        }

        var missingRequiredHeaders = GetMissingRequiredHeaders(headers, headerOptions.RequiredHeaders, domain?.RequiredHeaders);
        return new(headers, missingRequiredHeaders);
    }

    public string GetRequestFingerprint(Uri uri)
    {
        var requestHeaders = GetHeaders(uri);
        var builder = new StringBuilder();
        builder.Append("GET\n");
        builder.Append(uri.AbsoluteUri).Append('\n');

        foreach (var (name, value) in requestHeaders.Headers.OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(name.ToLowerInvariant()).Append(':').Append(value).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    public void ApplyHeaders(HttpRequestMessage request)
    {
        if (request.RequestUri is null)
        {
            return;
        }

        var requestHeaders = GetHeaders(request.RequestUri);
        if (requestHeaders.MissingRequiredHeaders.Count > 0)
        {
            throw MissingDownloadCredentialsException.ForMissingHeaders(request.RequestUri.Host, requestHeaders.MissingRequiredHeaders);
        }

        foreach (var (name, value) in requestHeaders.Headers)
        {
            request.Headers.Remove(name);
            request.Headers.TryAddWithoutValidation(name, value);
        }
    }

    public static string RedactHeaderValue(string name, string value)
    {
        return SensitiveHeaderNames.Contains(name) ? "[redacted]" : value;
    }

    private static DownloadDomainRequestHeaderOptions? GetMatchingDomainOptions(
        string host,
        IReadOnlyDictionary<string, DownloadDomainRequestHeaderOptions> domains)
    {
        DownloadDomainRequestHeaderOptions? bestMatch = null;
        var bestMatchLength = -1;

        foreach (var (pattern, domainOptions) in domains)
        {
            if (!HostMatchesPattern(host, pattern))
            {
                continue;
            }

            var matchLength = pattern.TrimStart('*', '.').Length;
            if (matchLength <= bestMatchLength)
            {
                continue;
            }

            bestMatch = domainOptions;
            bestMatchLength = matchLength;
        }

        return bestMatch;
    }

    private static bool HostMatchesPattern(string host, string pattern)
    {
        if (host.Equals(pattern, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalizedPattern = pattern.StartsWith("*.", StringComparison.Ordinal)
            ? pattern[2..]
            : pattern.TrimStart('.');

        if (normalizedPattern.Length == pattern.Length)
        {
            return false;
        }

        return host.EndsWith($".{normalizedPattern}", StringComparison.OrdinalIgnoreCase)
               || host.Equals(normalizedPattern, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddHeader(Dictionary<string, string> headers, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        headers[name] = value;
    }

    private static List<string> GetMissingRequiredHeaders(
        Dictionary<string, string> headers,
        IEnumerable<string> globalRequiredHeaders,
        IEnumerable<string>? domainRequiredHeaders)
    {
        return globalRequiredHeaders
            .Concat(domainRequiredHeaders ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => !headers.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
            .ToList();
    }
}

public sealed record DownloadRequestHeaders(
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyList<string> MissingRequiredHeaders);
