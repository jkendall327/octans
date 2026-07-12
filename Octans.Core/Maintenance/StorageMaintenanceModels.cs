using Octans.Data.Models.Maintenance;

namespace Octans.Core.Maintenance;

public sealed record StorageMaintenanceJobDto(
    Guid Id,
    StorageMaintenanceJobType Type,
    StorageMaintenanceJobStatus Status,
    StorageMaintenanceTrigger Trigger,
    StorageRepairActions RepairActions,
    Guid? SourceScanJobId,
    int TotalItems,
    int ProcessedItems,
    int FindingsCount,
    int RepairedItems,
    int FailedItems,
    long ScannedBytes,
    string? CurrentItem,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset UpdatedAt);

public sealed record StorageMaintenanceFindingDto(
    Guid Id,
    Guid ScanJobId,
    StorageFindingType Type,
    StorageFindingSeverity Severity,
    string? Hash,
    string? Path,
    string? ExpectedPath,
    long? Size,
    string Message,
    StorageFindingResolution Resolution,
    Guid? RepairJobId,
    DateTimeOffset? ResolvedAt,
    string? ResolutionMessage);

public sealed record StorageMaintenanceFindingsPage(
    int Total,
    IReadOnlyList<StorageMaintenanceFindingDto> Items);

public sealed record StorageRepairRequest(StorageRepairActions Actions);

public sealed record StorageMaintenanceJobCreated(Guid JobId);
