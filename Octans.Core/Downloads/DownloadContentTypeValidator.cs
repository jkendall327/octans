using System.Net.Http.Headers;
using Octans.Core.Downloads.Models;
using Octans.Data.Models;

namespace Octans.Core.Downloads;

internal sealed record DownloadContentTypeValidationResult(
    bool Accepted,
    string? Message = null,
    string? ResponseContentType = null);

internal static class DownloadContentTypeValidator
{
    public static DownloadContentTypeValidationResult Validate(
        QueuedDownload download,
        MediaTypeHeaderValue? responseContentType,
        DownloadContentTypeValidationOptions options)
    {
        var allowed = GetAllowedContentTypes(download, options);
        if (allowed.Length == 0)
        {
            return new(true);
        }

        var mediaType = responseContentType?.MediaType?.Trim();
        var responseValue = responseContentType?.ToString();
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            return options.AllowMissingContentType
                ? new(true)
                : new(false, $"Download content type was missing; expected {FormatAllowed(allowed)}.", responseValue);
        }

        if (allowed.Any(allowedContentType => Matches(allowedContentType, mediaType)))
        {
            return new(true);
        }

        if (options.AllowGenericContentType &&
            options.GenericContentTypes.Any(contentType => Matches(contentType, mediaType)))
        {
            return new(true);
        }

        return new(false,
            $"Download content type '{responseValue ?? mediaType}' did not match expected {FormatAllowed(allowed)}.",
            responseValue ?? mediaType);
    }

    private static string[] GetAllowedContentTypes(
        QueuedDownload download,
        DownloadContentTypeValidationOptions options)
    {
        var explicitTypes = DownloadContentTypeList.Deserialize(download.AllowedContentTypes);
        if (explicitTypes.Length > 0 || !options.InferContentTypesFromDestinationPath)
        {
            return explicitTypes;
        }

        var extension = Path.GetExtension(download.DestinationPath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return [];
        }

        foreach (var (configuredExtension, contentTypes) in options.ContentTypesByExtension)
        {
            if (string.Equals(configuredExtension, extension, StringComparison.OrdinalIgnoreCase))
            {
                return contentTypes;
            }
        }

        return [];
    }

    private static bool Matches(string expected, string actual)
    {
        var normalizedExpected = Normalize(expected);
        var normalizedActual = Normalize(actual);

        if (string.Equals(normalizedExpected, "*/*", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedExpected, normalizedActual, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var expectedParts = normalizedExpected.Split('/');
        var actualParts = normalizedActual.Split('/');
        return expectedParts.Length == 2 &&
               actualParts.Length == 2 &&
               string.Equals(expectedParts[0], actualParts[0], StringComparison.OrdinalIgnoreCase) &&
               expectedParts[1] == "*";
    }

    private static string Normalize(string contentType)
    {
        var semicolon = contentType.IndexOf(';', StringComparison.Ordinal);
        return (semicolon >= 0 ? contentType[..semicolon] : contentType).Trim();
    }

    private static string FormatAllowed(IEnumerable<string> allowed)
    {
        return string.Join(", ", allowed.Select(contentType => $"'{contentType}'"));
    }
}

/// <summary>
/// Raised when a response content type does not match the request's accepted types.
/// </summary>
public sealed class DownloadContentTypeException : Exception
{
    public DownloadContentTypeException()
    {
    }

    public DownloadContentTypeException(string message) : base(message)
    {
    }

    public DownloadContentTypeException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public DownloadContentTypeException(string message, string? responseContentType) : base(message)
    {
        ResponseContentType = responseContentType;
    }

    public string? ResponseContentType { get; }
}
