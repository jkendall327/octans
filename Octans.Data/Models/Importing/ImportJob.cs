using System.ComponentModel.DataAnnotations;

namespace Octans.Core.Importing.Jobs;

public class ImportJob
{
    [Key]
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public ImportJobStatus Status { get; set; }
    public required string SerializedRequest { get; set; }
    public List<ImportItem> Items { get; set; } = [];
}

public enum ImportJobStatus
{
    Queued,
    InProgress,
    Paused,
    Completed,
    Failed
}
