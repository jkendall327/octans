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

public record SubscriptionExecutionResult(int ItemsFound);
