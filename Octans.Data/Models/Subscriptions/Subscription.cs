using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Octans.Data.Models;

namespace Octans.Data.Models.Subscriptions;

public class Subscription
{
    [Key]
    public int Id { get; init; }

    [MaxLength(100)]
    public required string Name { get; set; }
    public TimeSpan CheckPeriod { get; set; }

    [MaxLength(500)]
    public required string Query { get; set; }
    public int RepositoryId { get; set; } = (int)RepositoryType.Inbox;
    public bool AllowReimportDeleted { get; set; }
    public bool AutoArchive { get; set; }
    public string? SerializedTags { get; set; }
    public int ProviderId { get; set; }
    [ForeignKey(nameof(ProviderId))]
    public Provider Provider { get; set; } = null!;
    public DateTimeOffset NextCheck { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsRunning { get; set; }
    public DateTimeOffset? LastStartedAt { get; set; }
    public DateTimeOffset? LastCompletedAt { get; set; }
    public string? LastError { get; set; }
    public int ConsecutiveFailures { get; set; }
    public int MaxItemsPerRun { get; set; } = 100;
    public string? Cursor { get; set; }
    public ICollection<SubscriptionExecution> Executions { get; } = new List<SubscriptionExecution>();
    public ICollection<SubscriptionSourceItem> SourceItems { get; } = new List<SubscriptionSourceItem>();
}
