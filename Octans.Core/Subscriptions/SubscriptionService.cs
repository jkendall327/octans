using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Octans.Core.Progress;
using Octans.Data.Models;
using Octans.Data.Models.Subscriptions;

namespace Octans.Core.Subscriptions;

public interface ISubscriptionService
{
    Task CheckAndExecute(CancellationToken stoppingToken = default);
    Task<List<SubscriptionStatusDto>> GetAllAsync();
    Task AddAsync(string name, string downloaderName, string query, TimeSpan frequency);
    Task DeleteAsync(int id);
}

internal sealed class SubscriptionService(
    IDbContextFactory<ServerDbContext> factory,
    TimeProvider timeProvider,
    IBackgroundProgressReporter reporter,
    ISubscriptionExecutor executor,
    ILogger<SubscriptionService> logger) : ISubscriptionService
{
    public async Task CheckAndExecute(CancellationToken stoppingToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(stoppingToken);

        var now = timeProvider.GetUtcNow();

        var subscriptions = await db
            .Subscriptions
            .Where(s => s.NextCheck <= now)
            .ToListAsync(stoppingToken);

        await reporter.ReportMessage($"Executing {subscriptions.Count} subscriptions...");

        foreach (var subscription in subscriptions)
        {
            try
            {
                var result = await executor.ExecuteAsync(subscription, stoppingToken);

                var execution = new SubscriptionExecution
                {
                    SubscriptionId = subscription.Id,
                    ExecutedAt = now,
                    Status = SubscriptionExecutionStatus.Succeeded,
                    ItemsFound = result.ItemsFound
                };
                db.SubscriptionExecutions.Add(execution);

                logger.LogInformation("Executed subscription {Name}", subscription.Name);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var execution = new SubscriptionExecution
                {
                    SubscriptionId = subscription.Id,
                    ExecutedAt = now,
                    Status = SubscriptionExecutionStatus.Failed,
                    ErrorMessage = ex.Message
                };
                db.SubscriptionExecutions.Add(execution);

                logger.LogError(
                    ex,
                    "Subscription {SubscriptionId} ({Name}) failed while executing",
                    subscription.Id,
                    subscription.Name);
            }

            subscription.NextCheck = now.Add(subscription.CheckPeriod);
            await db.SaveChangesAsync(stoppingToken);
        }
    }

    public async Task<List<SubscriptionStatusDto>> GetAllAsync()
    {
        await using var db = await factory.CreateDbContextAsync();

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
                lastExecution?.Status,
                lastExecution?.ItemsFound,
                lastExecution?.ErrorMessage,
                s.NextCheck
            );
        }).ToList();
    }

    public async Task AddAsync(string name, string downloaderName, string query, TimeSpan frequency)
    {
        await using var db = await factory.CreateDbContextAsync();

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
            NextCheck = timeProvider.GetUtcNow()
        };

        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();

        var subscription = await db.Subscriptions.FindAsync(id);
        if (subscription is null) return;

        db.Subscriptions.Remove(subscription);
        await db.SaveChangesAsync();
    }
}
