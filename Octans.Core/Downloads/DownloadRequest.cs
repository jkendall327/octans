namespace Octans.Core.Downloads;

public class DownloadRequest
{
    public required string Url { get; set; }
    public required string DestinationPath { get; set; }

    /// <summary>
    /// Higher numbers = higher priority
    /// </summary>
    public int Priority { get; set; }
}