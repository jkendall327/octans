namespace Octans.Core.Downloads.Models;

public class DownloadManagerOptions
{
    public int MaxConcurrentDownloads { get; set; } = 3;
    public int MaxConcurrentDownloadsPerDomain { get; set; } = 2;
    public DownloadDiskSpaceOptions DiskSpace { get; set; } = new();
    public DownloadContentTypeValidationOptions ContentTypeValidation { get; set; } = new();
    public DownloadHostCircuitBreakerOptions HostCircuitBreaker { get; set; } = new();
}

public class DownloadHostCircuitBreakerOptions
{
    public double FailureRatio { get; set; } = 0.5;
    public int MinimumThroughput { get; set; } = 5;
    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxRetryAttempts { get; set; } = 2;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(250);
}

public class DownloadDiskSpaceOptions
{
    public bool Enabled { get; set; } = true;
    public long RequiredFreeSpaceHeadroomBytes { get; set; } = 100L * 1024 * 1024;
}

public class DownloadContentTypeValidationOptions
{
    public bool InferContentTypesFromDestinationPath { get; set; } = true;
    public bool AllowMissingContentType { get; set; } = true;
    public bool AllowGenericContentType { get; set; } = true;
    public ICollection<string> GenericContentTypes { get; } =
    [
        "application/octet-stream",
        "binary/octet-stream"
    ];

    public Dictionary<string, string[]> ContentTypesByExtension { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        [".avif"] = ["image/*"],
        [".bmp"] = ["image/*"],
        [".gif"] = ["image/*"],
        [".heic"] = ["image/*"],
        [".heif"] = ["image/*"],
        [".jpeg"] = ["image/*"],
        [".jpg"] = ["image/*"],
        [".png"] = ["image/*"],
        [".svg"] = ["image/*"],
        [".webp"] = ["image/*"],
        [".7z"] = ["application/x-7z-compressed"],
        [".bz2"] = ["application/x-bzip2"],
        [".gz"] = ["application/gzip", "application/x-gzip"],
        [".rar"] = ["application/vnd.rar", "application/x-rar-compressed"],
        [".tar"] = ["application/x-tar"],
        [".tgz"] = ["application/gzip", "application/x-gzip"],
        [".xz"] = ["application/x-xz"],
        [".zip"] = ["application/zip", "application/x-zip-compressed"]
    };
}
