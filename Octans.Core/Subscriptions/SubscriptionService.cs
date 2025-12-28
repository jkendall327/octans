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
        using var db = await factory.CreateDbContextAsync(stoppingToken);

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

    public async Task<List<SubscriptionStatusDto>> GetAllAsync()
    {
        using var db = await factory.CreateDbContextAsync();

        var subscriptions = await db.Subscriptions
            .Include(s => s.Provider)
            .Include(s => s.Executions)
            .AsNoTracking()
            .ToListAsync();

        return subscriptions.Select(s =>
        {
            var lastExecution = s.Executions.OrderByDescending(e => e.ExecutedAt).FirstOrDefault();
            return new SubscriptionStatusDto(
                s.Id,
                s.Name,
                s.Provider.Name,
                s.Query,
                s.CheckPeriod,
                lastExecution?.ExecutedAt,
                lastExecution?.ItemsFound,
                s.NextCheck
            );
        }).ToList();
    }

    public async Task AddAsync(string name, string downloaderName, string query, TimeSpan frequency)
    {
        using var db = await factory.CreateDbContextAsync();

        var provider = await db.Providers.FirstOrDefaultAsync(p => p.Name == downloaderName);
        if (provider is null)
        {
            provider = new Provider { Name = downloaderName };
            db.Providers.Add(provider);
        }

        var subscription = new Subscription
        {
            Name = name,
            Provider = provider,
            Query = query,
            CheckPeriod = frequency,
            NextCheck = timeProvider.GetUtcNow().UtcDateTime
        };

        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        using var db = await factory.CreateDbContextAsync();

        var subscription = await db.Subscriptions.FindAsync(id);
        if (subscription is null) return;

        db.Subscriptions.Remove(subscription);
        await db.SaveChangesAsync();
    }
}