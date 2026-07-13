using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Octans.Data.Models.Subscriptions;

/// <summary>
/// Durable source-level history for a subscription. This is deliberately
/// independent of content hashes: a site post can change its media URL and
/// still be the same source item.
/// </summary>
public class SubscriptionSourceItem
{
    [Key]
    public long Id { get; set; }

    public int SubscriptionId { get; set; }

    [ForeignKey(nameof(SubscriptionId))]
    public Subscription Subscription { get; set; } = null!;

    public int? FirstExecutionId { get; set; }

    [ForeignKey(nameof(FirstExecutionId))]
    public SubscriptionExecution? FirstExecution { get; set; }

    public int? LastExecutionId { get; set; }

    [ForeignKey(nameof(LastExecutionId))]
    public SubscriptionExecution? LastExecution { get; set; }

    [MaxLength(500)]
    public required string SourceId { get; set; }

    [MaxLength(2000)]
    public required string RemoteUrl { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? QueuedAt { get; set; }
    public DateTimeOffset? ImportedAt { get; set; }
}
