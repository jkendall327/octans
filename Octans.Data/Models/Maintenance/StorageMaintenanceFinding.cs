using System.ComponentModel.DataAnnotations;

namespace Octans.Data.Models.Maintenance;

public class StorageMaintenanceFinding
{
    [Key]
    public Guid Id { get; set; }
    public Guid ScanJobId { get; set; }
    public StorageMaintenanceJob ScanJob { get; set; } = null!;
    public StorageFindingType Type { get; set; }
    public StorageFindingSeverity Severity { get; set; }
    [MaxLength(64)]
    public string? Hash { get; set; }
    public string? Path { get; set; }
    public string? ExpectedPath { get; set; }
    public long? Size { get; set; }
    [MaxLength(2000)]
    public required string Message { get; set; }
    public StorageFindingResolution Resolution { get; set; }
    public Guid? RepairJobId { get; set; }
    public StorageMaintenanceJob? RepairJob { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    [MaxLength(2000)]
    public string? ResolutionMessage { get; set; }
}

public enum StorageFindingType
{
    MissingOriginal,
    MissingThumbnail,
    OrphanedOriginal,
    OrphanedThumbnail,
    MalformedStorageFile,
    MisplacedOriginal,
    MisplacedThumbnail,
    DuplicateOriginal,
    DuplicateThumbnail,
    ContentHashMismatch,
    ExtensionMismatch,
    ContentTypeMismatch
}

public enum StorageFindingSeverity
{
    Information,
    Warning,
    Error
}

public enum StorageFindingResolution
{
    Open,
    Resolved,
    Failed,
    NotRepairable
}
