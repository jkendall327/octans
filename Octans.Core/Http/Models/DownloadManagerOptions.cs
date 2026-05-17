namespace Octans.Core.Http.Models;

/// <summary>
/// Configuration for the durable HTTP download manager.
/// </summary>
public class DownloadManagerOptions
{
    public int MaxConcurrentDownloads { get; set; } = 3;
    public int MaxConcurrentDownloadsPerDomain { get; set; } = 2;
    public TimeSpan CompletionPollingInterval { get; set; } = TimeSpan.FromSeconds(2);
    public DownloadDiskSpaceOptions DiskSpace { get; set; } = new();
    public DownloadSizeLimitOptions SizeLimits { get; set; } = new();
    public DownloadContentTypeValidationOptions ContentTypeValidation { get; set; } = new();
    public DownloadHostCircuitBreakerOptions HostCircuitBreaker { get; set; } = new();
    public DownloadRequestHeaderOptions RequestHeaders { get; set; } = new();
}

/// <summary>
/// Global and per-domain headers to apply to outgoing download requests.
/// </summary>
public class DownloadRequestHeaderOptions
{
    public string DefaultUserAgent { get; set; } = "Octans/1.0";
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ICollection<string> RequiredHeaders { get; } = [];
    public Dictionary<string, DownloadDomainRequestHeaderOptions> Domains { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Request header overrides for one domain or wildcard domain pattern.
/// </summary>
public class DownloadDomainRequestHeaderOptions
{
    public string? UserAgent { get; set; }
    public string? Authorization { get; set; }
    public string? Cookie { get; set; }
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ICollection<string> RequiredHeaders { get; } = [];
}

/// <summary>
/// Resilience settings for per-host retry and circuit-breaker behavior.
/// </summary>
public class DownloadHostCircuitBreakerOptions
{
    public double FailureRatio { get; set; } = 0.5;
    public int MinimumThroughput { get; set; } = 5;
    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxRetryAttempts { get; set; } = 2;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(250);
}

/// <summary>
/// Free-space safety settings checked before and during downloads.
/// </summary>
public class DownloadDiskSpaceOptions
{
    public bool Enabled { get; set; } = true;
    public long RequiredFreeSpaceHeadroomBytes { get; set; } = 100L * 1024 * 1024;
}

/// <summary>
/// Maximum download size settings, with optional domain and source-type overrides.
/// </summary>
public class DownloadSizeLimitOptions
{
    public bool Enabled { get; set; } = true;
    public long MaxBytes { get; set; } = 10L * 1024 * 1024 * 1024;
    public Dictionary<string, long> MaxBytesByDomain { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, long> MaxBytesBySourceType { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Response content-type validation settings.
/// </summary>
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
