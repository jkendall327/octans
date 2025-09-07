using Octans.Core.Models;

namespace Octans.Client;

public interface ISubscriptionExecutor
{
    Task ExecuteAsync(Subscription subscription, CancellationToken cancellationToken);
}
