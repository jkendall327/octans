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
    private readonly Dictionary<string, DateTime> _subscriptions = new();

    public SubscriptionBackgroundService(
        IOptions<SubscriptionOptions> options,
        ILogger<SubscriptionBackgroundService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LoadSubscriptions();

        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var subscription in _subscriptions)
            {
                if (DateTime.Now >= subscription.Value)
                {
                    await ExecuteQueryAsync(subscription.Key);
                    UpdateSubscriptionTime(subscription.Key);
                }
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private void LoadSubscriptions()
    {
        foreach (var item in _options.Items)
        {
            _subscriptions[item.Name] = DateTime.Now.Add(item.Interval);
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

        _subscriptions[subscriptionName] = DateTime.Now.Add(item.Interval);
    }
}
