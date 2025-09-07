using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Octans.Core.Models;
using Octans.Core.Progress;

namespace Octans.Client;

public class SubscriptionBackgroundService(
    IServiceProvider serviceProvider,
    IBackgroundProgressReporter reporter,
    ILogger<SubscriptionBackgroundService> logger,
    TimeProvider timeProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        var executor = scope.ServiceProvider.GetRequiredService<ISubscriptionExecutor>();
        
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                
                
                var now = timeProvider.GetUtcNow()
                    .UtcDateTime;

                var subscriptions = await db
                    .Subscriptions
                    .Where(s => s.NextCheck <= now)
                    .ToListAsync(stoppingToken);

                await reporter.ReportMessage($"Executing {subscriptions.Count} subscriptions...");

                foreach (var subscription in subscriptions)
                {
                    await executor.ExecuteAsync(subscription, stoppingToken);
                    subscription.NextCheck = now.Add(subscription.CheckPeriod);
                    logger.LogInformation("Executed subscription {Name}", subscription.Name);
                }

                await db.SaveChangesAsync(stoppingToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Subscription processing failed");
        }
    }
}