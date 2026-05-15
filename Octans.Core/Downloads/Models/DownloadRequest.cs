namespace Octans.Core.Downloads.Models;

/// <summary>
/// Caller-supplied description of a download job to enqueue.
/// </summary>
public class DownloadRequest
{
    public required Uri Url { get; set; }
    public required string DestinationPath { get; set; }
    public ICollection<string> AllowedContentTypes { get; } = [];
    public ICollection<DownloadHashExpectation> ExpectedHashes { get; } = [];
    public string? DisplayName { get; set; }
    public string? SourceType { get; set; }
    public string? SourceId { get; set; }

    /// <summary>
    /// Higher numbers = higher priority
    /// </summary>
    public int Priority { get; set; }
}
