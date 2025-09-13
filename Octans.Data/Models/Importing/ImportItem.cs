using System.ComponentModel.DataAnnotations;

namespace Octans.Core.Importing.Jobs;

public class ImportItem
{
    [Key]
    public Guid Id { get; set; }
    public Guid ImportJobId { get; set; }
    public required string Source { get; set; }
    public ImportItemStatus Status { get; set; }
    public string? Error { get; set; }
    public ImportJob? ImportJob { get; set; }
}

public enum ImportItemStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}
