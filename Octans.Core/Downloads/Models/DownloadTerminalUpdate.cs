using Octans.Data.Models;

namespace Octans.Core.Downloads.Models;

/// <summary>
/// Metadata to persist when a download reaches a terminal state.
/// </summary>
public sealed record DownloadTerminalUpdate
{
    public required DownloadTerminalOutcome Outcome { get; init; }
    public DownloadFailureCategory? FailureCategory { get; init; }
    public int? HttpStatusCode { get; init; }
    public string? ResponseContentType { get; init; }
    public string? ResponseETag { get; init; }
    public DateTimeOffset? ResponseLastModified { get; init; }
    public string? ValidationMessage { get; init; }
}
