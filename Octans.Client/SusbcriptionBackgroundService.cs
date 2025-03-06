namespace Octans.Client;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class SubscriptionBackgroundService : BackgroundService
{
    private readonly SubscriptionOptions _options;
    private readonly ILogger<SubscriptionBackgroundService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, DateTimeOffset> _subscriptions = new();

    public SubscriptionBackgroundService(
        IOptions<SubscriptionOptions> options,
        ILogger<SubscriptionBackgroundService> logger,
        TimeProvider timeProvider)
    {
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LoadSubscriptions();

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = _timeProvider.GetUtcNow();

            var active = _subscriptions
                .Where(subscription => now >= subscription.Value);
            
            foreach (var subscription in active)
            {
                await ExecuteQueryAsync(subscription.Key);
                UpdateSubscriptionTime(subscription.Key);
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private void LoadSubscriptions()
    {
        var now = _timeProvider.GetUtcNow();
        
        foreach (var item in _options.Items)
        {
            _subscriptions[item.Name] = now.Add(item.Interval);
            _logger.LogInformation("Loaded subscription: {Name} with interval {Interval}", 
                item.Name, item.Interval);
        }
    }

    private async Task ExecuteQueryAsync(string subscriptionName)
    {
        var item = _options.Items.Find(s => s.Name == subscriptionName);
        if (item == null)
        {
            _logger.LogWarning("Subscription {Name} not found", subscriptionName);
            return;
        }

        // TODO: Execute the query against your endpoint
        _logger.LogInformation("Executing query for subscription {Name}: {Query}", 
            subscriptionName, item.Query);
        await Task.CompletedTask;
    }

    private void UpdateSubscriptionTime(string subscriptionName)
    {
        var item = _options.Items.Find(s => s.Name == subscriptionName);
        if (item == null)
        {
            _logger.LogWarning("Cannot update time for subscription {Name} - not found", subscriptionName);
            return;
        }

        _subscriptions[subscriptionName] = _timeProvider.GetUtcNow().Add(item.Interval);
    }
}
