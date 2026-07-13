using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Octans.Data.Models.Subscriptions;

public class SubscriptionExecution
{
    [Key]
    public int Id { get; init; }

    public int SubscriptionId { get; init; }

    [ForeignKey(nameof(SubscriptionId))]
    public Subscription Subscription { get; init; } = null!;

    public DateTimeOffset ExecutedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public SubscriptionExecutionStatus Status { get; set; }

    public int? ItemsFound { get; set; }

    public int ItemsQueued { get; set; }

    public int ItemsSkipped { get; set; }

    public Guid? ImportJobId { get; set; }

    [MaxLength(2000)]
    public string? Diagnostics { get; set; }

    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }

    public ICollection<SubscriptionSourceItem> SourceItems { get; } = new List<SubscriptionSourceItem>();
}

public enum SubscriptionExecutionStatus
{
    Succeeded = 0,
    Failed = 1,
    Running = 2,
    Cancelled = 3
}
