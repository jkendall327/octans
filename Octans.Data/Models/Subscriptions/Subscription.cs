using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Octans.Data.Models;

namespace Octans.Data.Models.Subscriptions;

public class Subscription
{
    [Key]
    public int Id { get; init; }

    [MaxLength(100)]
    public required string Name { get; init; }
    public TimeSpan CheckPeriod { get; init; }

    [MaxLength(500)]
    public required string Query { get; init; }
    public int RepositoryId { get; init; } = (int)RepositoryType.Inbox;
    public bool AllowReimportDeleted { get; init; }
    public bool AutoArchive { get; init; }
    public string? SerializedTags { get; init; }
    public int ProviderId { get; init; }
    [ForeignKey(nameof(ProviderId))]
    public Provider Provider { get; init; } = null!;
    public DateTimeOffset NextCheck { get; set; }
    public ICollection<SubscriptionExecution> Executions { get; } = new List<SubscriptionExecution>();
}
