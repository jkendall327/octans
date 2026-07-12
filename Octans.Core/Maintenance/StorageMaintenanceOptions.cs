using System.ComponentModel.DataAnnotations;

namespace Octans.Core.Maintenance;

public sealed class StorageMaintenanceOptions
{
    public const string ConfigurationSectionName = "StorageMaintenance";

    public bool AutomaticScansEnabled { get; set; } = true;

    [Range(1, 3650)]
    public int AutomaticScanIntervalDays { get; set; } = 7;

    [Range(1, 3600)]
    public int IdlePollSeconds { get; set; } = 30;

    [Range(1, 10000)]
    public int PersistenceBatchSize { get; set; } = 100;
}
