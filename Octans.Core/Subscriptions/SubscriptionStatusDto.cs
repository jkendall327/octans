namespace Octans.Core.Subscriptions;

public record SubscriptionStatusDto(
    int Id,
    string Name,
    string DownloaderName,
    string Query,
    TimeSpan Frequency,
    DateTimeOffset? LastRun,
    int? ItemsFound,
    DateTimeOffset NextCheck);
