using System.ComponentModel.DataAnnotations;

namespace Octans.Core.Downloaders;

public class QueuedDownload
{
    [Key]
    public Guid Id { get; set; }
    public required string Url { get; set; }
    public required string DestinationPath { get; set; }
    public DateTime QueuedAt { get; set; }
    public int Priority { get; set; }
    public required string Domain { get; set; }
}