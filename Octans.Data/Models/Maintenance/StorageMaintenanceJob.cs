using System.ComponentModel.DataAnnotations;

namespace Octans.Data.Models.Maintenance;

public class StorageMaintenanceJob
{
    [Key]
    public Guid Id { get; set; }
    public StorageMaintenanceJobType Type { get; set; }
    public StorageMaintenanceJobStatus Status { get; set; }
    public StorageMaintenanceTrigger Trigger { get; set; }
    public StorageRepairActions RepairActions { get; set; }
    public Guid? SourceScanJobId { get; set; }
    public StorageMaintenanceJob? SourceScanJob { get; set; }
    public int TotalItems { get; set; }
    public int ProcessedItems { get; set; }
    public int FindingsCount { get; set; }
    public int RepairedItems { get; set; }
    public int FailedItems { get; set; }
    public long ScannedBytes { get; set; }
    public string? CurrentItem { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ICollection<StorageMaintenanceFinding> Findings { get; } = new List<StorageMaintenanceFinding>();
}

public enum StorageMaintenanceJobType
{
    Scan,
    Repair
}

public enum StorageMaintenanceJobStatus
{
    Queued,
    Running,
    CancelRequested,
    Cancelled,
    Completed,
    Failed
}

public enum StorageMaintenanceTrigger
{
    Manual,
    Automatic
}

[Flags]
public enum StorageRepairActions
{
    None = 0,
    RegenerateMissingThumbnails = 1,
    RepairMetadata = 2,
    QuarantineUnsafeFiles = 4,
    AllSafe = RegenerateMissingThumbnails | RepairMetadata | QuarantineUnsafeFiles
}
