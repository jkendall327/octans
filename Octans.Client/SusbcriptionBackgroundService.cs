namespace Octans.Client;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class SubscriptionBackgroundService(IConfiguration configuration) : BackgroundService
{
    private readonly Dictionary<string, DateTime> _subscriptions = new();

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
        var subscriptionsSection = configuration.GetSection("Subscriptions");
        foreach (var subscription in subscriptionsSection.GetChildren())
        {
            var name = subscription["Name"] ?? throw new InvalidOperationException();
            var interval = TimeSpan.Parse(subscription["Interval"] ?? throw new InvalidOperationException());
            _subscriptions[name] = DateTime.Now.Add(interval);
        }
    }

    private async Task ExecuteQueryAsync(string subscriptionName)
    {
        var query = configuration[$"Subscriptions:{subscriptionName}:Query"];
        // TODO: Execute the query against your endpoint
        Console.WriteLine($"Executing query for subscription {subscriptionName}: {query}");
        await Task.CompletedTask;
    }

    private void UpdateSubscriptionTime(string subscriptionName)
    {
        var input = configuration.GetValue<TimeSpan>($"Subscriptions:{subscriptionName}:Interval");
        _subscriptions[subscriptionName] = DateTime.Now.Add(input);
    }
}