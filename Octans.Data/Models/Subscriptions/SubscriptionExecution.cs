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

    public DateTimeOffset ExecutedAt { get; init; }

    public SubscriptionExecutionStatus Status { get; init; }

    public int? ItemsFound { get; init; }

    [MaxLength(2000)]
    public string? ErrorMessage { get; init; }
}

public enum SubscriptionExecutionStatus
{
    Succeeded,
    Failed
}
