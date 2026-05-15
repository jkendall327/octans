using Octans.Data.Models;

namespace Octans.Core.Downloads.Models;

/// <summary>
/// Terminal result returned for a completed, failed, or canceled download job.
/// </summary>
public sealed record DownloadJobResult
{
    public required Guid DownloadId { get; init; }
    public required DownloadTerminalOutcome Outcome { get; init; }
    public required string Url { get; init; }
    public required string DestinationPath { get; init; }
    public string? DisplayName { get; init; }
    public string? SourceType { get; init; }
    public string? SourceId { get; init; }
    public long BytesDownloaded { get; init; }
    public long TotalBytes { get; init; }
    public int? HttpStatusCode { get; init; }
    public string? ResponseContentType { get; init; }
    public string? ResponseETag { get; init; }
    public DateTimeOffset? ResponseLastModified { get; init; }
    public DownloadFailureCategory? FailureCategory { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ValidationMessage { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}
