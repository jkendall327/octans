using Octans.Core.Communication;

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
    private readonly IOctansApi _api;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, DateTimeOffset> _subscriptions = new();

    public SubscriptionBackgroundService(
        IOptions<SubscriptionOptions> options,
        ILogger<SubscriptionBackgroundService> logger,
        TimeProvider timeProvider,
        IOctansApi api)
    {
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider;
        _api = api;
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

    private async Task ExecuteQueryAsync(string name)
    {
        var item = _options.Items.Find(s => s.Name == name);
        if (item == null)
        {
            _logger.LogWarning("Subscription {Name} not found", name);
            return;
        }

        _logger.LogInformation("Executing query for subscription {Name}: {Query}", name, item.Query);

        var response = await _api.SubmitSubscription(new()
        {
            Name = item.Name,
            Query = item.Query
        });

        _logger.LogInformation("Got response for subscription: {@SubscriptionResponse}", response);

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
