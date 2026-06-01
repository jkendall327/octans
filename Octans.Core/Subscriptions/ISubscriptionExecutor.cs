using Octans.Data.Models.Subscriptions;

namespace Octans.Core.Subscriptions;

public interface ISubscriptionExecutor
{
    Task<SubscriptionExecutionResult> ExecuteAsync(Subscription subscription, CancellationToken cancellationToken);
}

internal sealed class NoOpSubscriptionExecutor : ISubscriptionExecutor
{
    public Task<SubscriptionExecutionResult> ExecuteAsync(Subscription subscription,
        CancellationToken cancellationToken) =>
        Task.FromResult(new SubscriptionExecutionResult(0));
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
}

public sealed record SubscriptionDiscoveredItem(
    string SourceId,
    Uri RemoteUrl);
