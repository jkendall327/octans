namespace Octans.Core.Downloaders;

public class BandwidthLimitExceededException : Exception
{
    public string Domain { get; }
    public long BytesPerHour { get; }
    public TimeSpan RetryAfter { get; }

    public BandwidthLimitExceededException(string domain, long bytesPerHour, TimeSpan retryAfter)
        : base($"Bandwidth limit of {bytesPerHour} bytes per hour exceeded for domain: {domain}. Retry after {retryAfter}.")
    {
        Domain = domain;
        BytesPerHour = bytesPerHour;
        RetryAfter = retryAfter;
    }
}