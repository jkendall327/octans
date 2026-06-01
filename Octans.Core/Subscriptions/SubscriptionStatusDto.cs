using Octans.Data.Models.Subscriptions;

namespace Octans.Core.Subscriptions;

public record SubscriptionStatusDto(
    int Id,
    string Name,
    string DownloaderName,
    string Query,
    TimeSpan Frequency,
    DateTimeOffset? LastRun,
    SubscriptionExecutionStatus? LastExecutionStatus,
    int? ItemsFound,
    string? LastError,
    DateTimeOffset NextCheck);
