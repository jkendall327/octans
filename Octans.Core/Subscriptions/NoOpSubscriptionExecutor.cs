using Octans.Core.Models;

namespace Octans.Client;

public class NoOpSubscriptionExecutor : ISubscriptionExecutor
{
    public Task ExecuteAsync(Subscription subscription, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
