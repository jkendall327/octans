using System.ComponentModel.DataAnnotations;

namespace Octans.Data.Models.Importing;

public class ImportItem
{
    [Key]
    public Guid Id { get; set; }
    public Guid ImportJobId { get; set; }
    public ImportType ImportType { get; set; }
    public required string Source { get; set; }
    public string? SerializedTags { get; set; }
    public ImportItemStatus Status { get; set; }
    public string? Error { get; set; }
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ImportJob? ImportJob { get; set; }
}

public enum ImportItemStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Skipped,
    Cancelled
}

public enum ImportType
{
    File,
    RawUrl
}
