namespace Octans.Core.Downloads.Models;

public class DownloadRequest
{
    public required Uri Url { get; set; }
    public required string DestinationPath { get; set; }
    public ICollection<string> AllowedContentTypes { get; } = [];
    public string? DisplayName { get; set; }
    public string? SourceType { get; set; }
    public string? SourceId { get; set; }

    /// <summary>
    /// Higher numbers = higher priority
    /// </summary>
    public int Priority { get; set; }
}
