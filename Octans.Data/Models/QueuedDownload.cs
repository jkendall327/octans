using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Octans.Data.Models;

[SuppressMessage("Design", "CA1056:URI-like properties should not be strings")]
public class QueuedDownload
{
    [Key]
    public Guid Id { get; set; }
    public required string Url { get; set; }
    public required string DestinationPath { get; set; }
    public DateTimeOffset QueuedAt { get; set; }
    public int Priority { get; set; }
    public required string Domain { get; set; }
}