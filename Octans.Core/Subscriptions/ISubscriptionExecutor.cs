using Octans.Data.Models.Subscriptions;

namespace Octans.Core.Subscriptions;

public interface ISubscriptionExecutor
{
    Task<SubscriptionExecutionResult> ExecuteAsync(Subscription subscription, CancellationToken cancellationToken);
}

public sealed record SubscriptionExecutionResult
{
    public SubscriptionExecutionResult(int itemsFound)
    {
        ItemsFound = itemsFound;
        DiscoveredItems = [];
    }

    public SubscriptionExecutionResult(IReadOnlyList<SubscriptionDiscoveredItem> discoveredItems)
    {
        ItemsFound = discoveredItems.Count;
        DiscoveredItems = discoveredItems;
    }

    public int ItemsFound { get; }
    public IReadOnlyList<SubscriptionDiscoveredItem> DiscoveredItems { get; }
    public string? NextCursor { get; init; }
    public string? Diagnostics { get; init; }
}

public sealed record SubscriptionDiscoveredItem(
    string SourceId,
    Uri RemoteUrl);

internal sealed class DownloaderSubscriptionExecutor(
    Octans.Core.Downloaders.IDownloaderDiscoveryService discoveryService) : ISubscriptionExecutor
{
    public async Task<SubscriptionExecutionResult> ExecuteAsync(
        Subscription subscription,
        CancellationToken cancellationToken)
    {
        var result = await discoveryService.DiscoverAsync(
            subscription.Provider.Name,
            subscription.Query,
            subscription.MaxItemsPerRun,
            cancellationToken);

        var discovered = result
            .Select(item => new SubscriptionDiscoveredItem(item.SourceId, item.RemoteUrl))
            .ToList();

        return new SubscriptionExecutionResult(discovered);
    }
}
