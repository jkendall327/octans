namespace Octans.Core.Downloads;

public sealed class DownloadSizeLimitException : Exception
{
    public DownloadSizeLimitException()
    {
    }

    public DownloadSizeLimitException(string message) : base(message)
    {
    }

    public DownloadSizeLimitException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public static DownloadSizeLimitException ForReportedSize(long reportedBytes, long maxBytes)
    {
        return new(
            $"Download reported {reportedBytes} bytes, which exceeds the configured max download size of {maxBytes} bytes.");
    }

    public static DownloadSizeLimitException ForReceivedSize(long receivedBytes, long maxBytes)
    {
        return new(
            $"Download exceeded the configured max download size of {maxBytes} bytes after receiving {receivedBytes} bytes.");
    }
}
