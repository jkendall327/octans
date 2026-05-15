using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Octans.Data.Models;

[SuppressMessage("Design", "CA1056:URI-like properties should not be strings")]
public class DownloadStatus
{
    [Key]
    public Guid Id { get; set; }
    public required string Url { get; set; }
    public required string Filename { get; set; }
    public string? DisplayName { get; set; }
    public required string DestinationPath { get; set; }
    public string? AllowedContentTypes { get; set; }
    public int Priority { get; set; }
    public long TotalBytes { get; set; }
    public long BytesDownloaded { get; set; }
    public double ProgressPercentage => TotalBytes > 0 ? (double)BytesDownloaded / TotalBytes * 100 : 0;
    public double CurrentSpeed { get; set; } // bytes per second
    public DownloadState State { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset LastUpdated { get; set; }
    public string? ErrorMessage { get; set; }
    public DownloadTerminalOutcome? TerminalOutcome { get; set; }
    public DownloadFailureCategory? FailureCategory { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? ResponseContentType { get; set; }
    public string? ResponseETag { get; set; }
    public DateTimeOffset? ResponseLastModified { get; set; }
    public string? ValidationMessage { get; set; }
    public required string Domain { get; set; }
    public string? SourceType { get; set; }
    public string? SourceId { get; set; }
    public string? RequestFingerprint { get; set; }
}
