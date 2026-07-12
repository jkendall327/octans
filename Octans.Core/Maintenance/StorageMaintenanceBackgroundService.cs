using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octans.Data.Models;
using Octans.Data.Models.Importing;
using Octans.Data.Models.Maintenance;

namespace Octans.Core.Maintenance;

internal sealed class StorageMaintenanceBackgroundService(
    StorageMaintenanceCoordinator coordinator,
    IOptions<StorageMaintenanceOptions> options,
    TimeProvider timeProvider,
    ILogger<StorageMaintenanceBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await coordinator.RecoverAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await coordinator.RunOnceIfIdleAsync(stoppingToken))
                {
                    continue;
                }

                await Task.Delay(TimeSpan.FromSeconds(options.Value.IdlePollSeconds), timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Storage maintenance background loop failed");
                await Task.Delay(TimeSpan.FromSeconds(options.Value.IdlePollSeconds), timeProvider, stoppingToken);
            }
        }
    }
}

internal sealed class StorageMaintenanceCoordinator(
    IServiceProvider serviceProvider,
    StorageMaintenanceProcessor processor,
    IOptions<StorageMaintenanceOptions> options,
    TimeProvider timeProvider)
{
    public Task RecoverAsync(CancellationToken cancellationToken = default) =>
        processor.RecoverInterruptedJobsAsync(cancellationToken);

    public async Task<bool> RunOnceIfIdleAsync(CancellationToken cancellationToken = default)
    {
        if (!await IsApplicationIdle(cancellationToken))
        {
            return false;
        }

        await QueueAutomaticScanIfDue(cancellationToken);
        return await processor.ProcessNextAsync(cancellationToken);
    }

    private async Task<bool> IsApplicationIdle(CancellationToken cancellationToken)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        var importBusy = await db.ImportJobs.AnyAsync(j =>
            j.Status == ImportJobStatus.Queued ||
            j.Status == ImportJobStatus.Running ||
            j.Status == ImportJobStatus.PauseRequested ||
            j.Status == ImportJobStatus.CancelRequested,
            cancellationToken);
        if (importBusy)
        {
            return false;
        }

        return !await db.DownloadStatuses.AnyAsync(d =>
            d.State == DownloadState.Queued ||
            d.State == DownloadState.WaitingForBandwidth ||
            d.State == DownloadState.InProgress,
            cancellationToken);
    }

    private async Task QueueAutomaticScanIfDue(CancellationToken cancellationToken)
    {
        if (!options.Value.AutomaticScansEnabled)
        {
            return;
        }

        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        if (await db.StorageMaintenanceJobs.AnyAsync(j =>
                j.Status == StorageMaintenanceJobStatus.Queued ||
                j.Status == StorageMaintenanceJobStatus.Running ||
                j.Status == StorageMaintenanceJobStatus.CancelRequested,
                cancellationToken))
        {
            return;
        }

        var lastScan = await db.StorageMaintenanceJobs
            .Where(j => j.Type == StorageMaintenanceJobType.Scan &&
                        j.Status == StorageMaintenanceJobStatus.Completed)
            .MaxAsync(j => (DateTimeOffset?)j.CompletedAt, cancellationToken);
        var dueAt = lastScan?.AddDays(options.Value.AutomaticScanIntervalDays);
        if (dueAt is not null && dueAt > timeProvider.GetUtcNow())
        {
            return;
        }

        var service = scope.ServiceProvider.GetRequiredService<IStorageMaintenanceService>();
        await service.QueueScanAsync(StorageMaintenanceTrigger.Automatic, cancellationToken);
    }
}
