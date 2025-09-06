using System.Threading;
using System.Threading.Tasks;
using Octans.Core.Models;

namespace Octans.Client;

public interface ISubscriptionExecutor
{
    Task ExecuteAsync(Subscription subscription, CancellationToken cancellationToken);
}
