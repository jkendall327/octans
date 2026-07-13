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
    DateTimeOffset NextCheck,
    bool IsEnabled = true,
    bool IsRunning = false,
    DateTimeOffset? LastCompletedAt = null,
    string? CurrentError = null,
    int ConsecutiveFailures = 0,
    int? ItemsQueued = null,
    int? ItemsSkipped = null,
    int MaxItemsPerRun = 100);

public sealed record SubscriptionExecutionDto(
    int Id,
    int SubscriptionId,
    DateTimeOffset ExecutedAt,
    DateTimeOffset? CompletedAt,
    SubscriptionExecutionStatus Status,
    int? ItemsFound,
    int ItemsQueued,
    int ItemsSkipped,
    Guid? ImportJobId,
    string? ErrorMessage,
    string? Diagnostics);

public sealed record SubscriptionSourceItemDto(
    long Id,
    string SourceId,
    string RemoteUrl,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? QueuedAt,
    DateTimeOffset? ImportedAt,
    int? LastExecutionId);
