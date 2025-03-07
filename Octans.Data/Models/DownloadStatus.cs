using System.ComponentModel.DataAnnotations;

namespace Octans.Core.Downloaders;

public class DownloadStatus
{
    [Key]
    public Guid Id { get; set; }
    public required string Url { get; set; }
    public required string Filename { get; set; }
    public required string DestinationPath { get; set; }
    public long TotalBytes { get; set; }
    public long BytesDownloaded { get; set; }
    public double ProgressPercentage => TotalBytes > 0 ? (double)BytesDownloaded / TotalBytes * 100 : 0;
    public double CurrentSpeed { get; set; } // bytes per second
    public DownloadState State { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime LastUpdated { get; set; }
    public string? ErrorMessage { get; set; }
    public required string Domain { get; set; }
}