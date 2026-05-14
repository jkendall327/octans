namespace Octans.Core.Downloads.Models;

public class DownloadManagerOptions
{
    public int MaxConcurrentDownloads { get; set; } = 3;
    public int MaxConcurrentDownloadsPerDomain { get; set; } = 2;
}
