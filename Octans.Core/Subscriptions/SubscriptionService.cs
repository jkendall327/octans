using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Octans.Client;
using Octans.Core.Models;
using Octans.Core.Progress;

namespace Octans.Core.Subscriptions;

public sealed class SubscriptionService(
    IDbContextFactory<ServerDbContext> factory,
    TimeProvider timeProvider,
    IBackgroundProgressReporter reporter,
    ISubscriptionExecutor executor,
    ILogger<SubscriptionService> logger)
{
    public async Task CheckAndExecute(CancellationToken stoppingToken = default)
    {
        var db = await factory.CreateDbContextAsync(stoppingToken);

        var now = timeProvider.GetUtcNow()
            .UtcDateTime;

        var subscriptions = await db
            .Subscriptions
            .Where(s => s.NextCheck <= now)
            .ToListAsync(stoppingToken);

        await reporter.ReportMessage($"Executing {subscriptions.Count} subscriptions...");

        foreach (var subscription in subscriptions)
        {
            var result = await executor.ExecuteAsync(subscription, stoppingToken);

            var execution = new SubscriptionExecution
            {
                SubscriptionId = subscription.Id,
                ExecutedAt = now,
                ItemsFound = result.ItemsFound
            };
            db.SubscriptionExecutions.Add(execution);

            subscription.NextCheck = now.Add(subscription.CheckPeriod);
            
            logger.LogInformation("Executed subscription {Name}", subscription.Name);
        }

        await db.SaveChangesAsync(stoppingToken);
    }
}