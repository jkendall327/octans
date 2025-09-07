using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Octans.Core.Subscriptions;

namespace Octans.Client;

public class SubscriptionBackgroundService(
    IServiceProvider serviceProvider,
    ILogger<SubscriptionBackgroundService> logger,
    TimeProvider timeProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<SubscriptionService>();

        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), timeProvider);

            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                await service.CheckAndExecute(stoppingToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Subscription processing failed");
        }
    }
}