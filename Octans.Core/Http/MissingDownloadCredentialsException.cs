namespace Octans.Core.Http;

/// <summary>
/// Raised when configured required headers for a download host are missing.
/// </summary>
public sealed class MissingDownloadCredentialsException : InvalidOperationException
{
    public MissingDownloadCredentialsException()
    {
    }

    public MissingDownloadCredentialsException(string message) : base(message)
    {
    }

    public MissingDownloadCredentialsException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public static MissingDownloadCredentialsException ForMissingHeaders(string domain, IReadOnlyList<string> headerNames)
    {
        var headers = string.Join(", ", headerNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
        return new($"Download credentials are missing for {domain}. Required request headers are not configured: {headers}.");
    }
}
