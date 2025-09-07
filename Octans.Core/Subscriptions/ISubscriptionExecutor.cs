using Octans.Core.Models;

namespace Octans.Client;

public interface ISubscriptionExecutor
{
    Task<SubscriptionExecutionResult> ExecuteAsync(Subscription subscription, CancellationToken cancellationToken);
}

public class NoOpSubscriptionExecutor : ISubscriptionExecutor
{
    public Task<SubscriptionExecutionResult> ExecuteAsync(Subscription subscription,
        CancellationToken cancellationToken) =>
        Task.FromResult(new SubscriptionExecutionResult(0));
}

public record SubscriptionExecutionResult(int ItemsFound);