using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Octans.Core.Models;

public class SubscriptionExecution
{
    [Key]
    public int Id { get; init; }

    public int SubscriptionId { get; init; }

    [ForeignKey(nameof(SubscriptionId))]
    public Subscription Subscription { get; init; } = null!;

    public DateTime ExecutedAt { get; init; }

    public int ItemsFound { get; init; }
}
