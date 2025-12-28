namespace Octans.Core.Subscriptions;

public record SubscriptionStatusDto(
    int Id,
    string Name,
    string DownloaderName,
    string Query,
    TimeSpan Frequency,
    DateTime? LastRun,
    int? ItemsFound,
    DateTime NextCheck);
