using System.ComponentModel.DataAnnotations;

namespace Octans.Data.Models.Importing;

public class ImportJob
{
    [Key]
    public Guid Id { get; set; }
    public ImportJobStatus Status { get; set; }
    public ImportJobPhase Phase { get; set; }
    public int TotalItems { get; set; }
    public int ProcessedItems { get; set; }
    public int FailedItems { get; set; }
    public string? CurrentItem { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool DeleteAfterImport { get; set; }
    public bool AllowReimportDeleted { get; set; }
    public bool AutoArchive { get; set; }
    public string? SerializedFilterData { get; set; }

    public string SerializedRequest { get; set; } = string.Empty;
    public List<ImportItem> Items { get; init; } = [];
}

public enum ImportJobStatus
{
    Queued,
    Running,
    PauseRequested,
    Paused,
    CancelRequested,
    Cancelled,
    Completed,
    Failed
}

public enum ImportJobPhase
{
    Scanning,
    Importing
}
