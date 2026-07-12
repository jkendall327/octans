using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octans.Data.Models;
using Octans.Data.Models.Maintenance;

namespace Octans.Core.Maintenance;

internal sealed class StorageMaintenanceProcessor(
    IServiceProvider serviceProvider,
    IOptions<StorageMaintenanceOptions> options,
    ILogger<StorageMaintenanceProcessor> logger)
{
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        var job = await db.StorageMaintenanceJobs
            .Where(j => j.Status == StorageMaintenanceJobStatus.Queued)
            .OrderBy(j => j.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (job is null)
        {
            return false;
        }

        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var now = timeProvider.GetUtcNow();
        job.Status = StorageMaintenanceJobStatus.Running;
        job.StartedAt ??= now;
        job.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            if (job.Type is StorageMaintenanceJobType.Scan)
            {
                await ProcessScan(scope.ServiceProvider, db, job, timeProvider, cancellationToken);
            }
            else
            {
                await ProcessRepair(scope.ServiceProvider, db, job, timeProvider, cancellationToken);
            }

            await db.Entry(job).ReloadAsync(cancellationToken);
            if (job.Status is StorageMaintenanceJobStatus.CancelRequested)
            {
                job.Status = StorageMaintenanceJobStatus.Cancelled;
            }
            else
            {
                job.Status = StorageMaintenanceJobStatus.Completed;
            }

            job.CurrentItem = null;
            job.CompletedAt = timeProvider.GetUtcNow();
            job.UpdatedAt = job.CompletedAt.Value;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            job.Status = StorageMaintenanceJobStatus.Cancelled;
            job.CurrentItem = null;
            job.CompletedAt = timeProvider.GetUtcNow();
            job.UpdatedAt = job.CompletedAt.Value;
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Storage maintenance job {JobId} failed", job.Id);
            job.Status = StorageMaintenanceJobStatus.Failed;
            job.FailureReason = ex.Message;
            job.CurrentItem = null;
            job.CompletedAt = timeProvider.GetUtcNow();
            job.UpdatedAt = job.CompletedAt.Value;
            await db.SaveChangesAsync(CancellationToken.None);
        }

        return true;
    }

    public async Task RecoverInterruptedJobsAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var jobs = await db.StorageMaintenanceJobs
            .Where(j => j.Status == StorageMaintenanceJobStatus.Running ||
                        j.Status == StorageMaintenanceJobStatus.CancelRequested)
            .ToListAsync(cancellationToken);

        foreach (var job in jobs)
        {
            if (job.Status is StorageMaintenanceJobStatus.CancelRequested)
            {
                job.Status = StorageMaintenanceJobStatus.Cancelled;
                job.CompletedAt = timeProvider.GetUtcNow();
                continue;
            }

            job.Status = StorageMaintenanceJobStatus.Queued;
            job.CurrentItem = null;
            job.FailureReason = null;
            if (job.Type is StorageMaintenanceJobType.Scan)
            {
                await db.StorageMaintenanceFindings.Where(f => f.ScanJobId == job.Id).ExecuteDeleteAsync(cancellationToken);
                job.TotalItems = 0;
                job.ProcessedItems = 0;
                job.FindingsCount = 0;
                job.ScannedBytes = 0;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessScan(
        IServiceProvider services,
        ServerDbContext db,
        StorageMaintenanceJob job,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        await db.StorageMaintenanceFindings.Where(f => f.ScanJobId == job.Id).ExecuteDeleteAsync(cancellationToken);
        var scanner = services.GetRequiredService<StorageInventoryScanner>();
        var findings = new List<StorageMaintenanceFinding>();
        var batchSize = options.Value.PersistenceBatchSize;

        async Task Flush(CancellationToken token)
        {
            if (findings.Count == 0)
            {
                return;
            }

            db.StorageMaintenanceFindings.AddRange(findings);
            await db.SaveChangesAsync(token);
            foreach (var finding in findings)
            {
                db.Entry(finding).State = EntityState.Detached;
            }

            findings.Clear();
        }

        await scanner.ScanAsync(
            async (finding, token) =>
            {
                finding.ScanJobId = job.Id;
                findings.Add(finding);
                if (findings.Count >= batchSize)
                {
                    await Flush(token);
                }
            },
            async (progress, token) =>
            {
                await ThrowIfCancellationRequested(db, job, token);
                job.TotalItems = progress.TotalItems;
                job.ProcessedItems = progress.ProcessedItems;
                job.FindingsCount = progress.Findings;
                job.ScannedBytes = progress.ScannedBytes;
                job.CurrentItem = progress.CurrentItem;
                job.UpdatedAt = timeProvider.GetUtcNow();
                await Flush(token);
                await db.SaveChangesAsync(token);
            },
            cancellationToken);

        await Flush(cancellationToken);
    }

    private async Task ProcessRepair(
        IServiceProvider services,
        ServerDbContext db,
        StorageMaintenanceJob job,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (job.SourceScanJobId is null)
        {
            throw new InvalidOperationException("Repair job has no source scan.");
        }

        var repairer = services.GetRequiredService<StorageRepairer>();
        var repairableTypes = StorageRepairer.GetRepairableTypes(job.RepairActions);
        job.TotalItems = await db.StorageMaintenanceFindings.CountAsync(
            f => f.ScanJobId == job.SourceScanJobId &&
                 f.Resolution == StorageFindingResolution.Open &&
                 repairableTypes.Contains(f.Type),
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        while (true)
        {
            var finding = await db.StorageMaintenanceFindings
                .Where(f => f.ScanJobId == job.SourceScanJobId &&
                            f.Resolution == StorageFindingResolution.Open &&
                            repairableTypes.Contains(f.Type))
                .OrderByDescending(f => f.Severity)
                .ThenBy(f => f.Type)
                .FirstOrDefaultAsync(cancellationToken);
            if (finding is null)
            {
                break;
            }

            await ThrowIfCancellationRequested(db, job, cancellationToken);
            job.CurrentItem = finding.Path ?? finding.Hash;
            job.UpdatedAt = timeProvider.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);

            try
            {
                finding.ResolutionMessage = await repairer.RepairAsync(finding, job.Id, cancellationToken);
                finding.Resolution = StorageFindingResolution.Resolved;
                job.RepairedItems++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                finding.Resolution = StorageFindingResolution.Failed;
                finding.ResolutionMessage = ex.Message;
                job.FailedItems++;
                logger.LogWarning(ex, "Could not repair storage finding {FindingId}", finding.Id);
            }

            finding.RepairJobId = job.Id;
            finding.ResolvedAt = timeProvider.GetUtcNow();
            job.ProcessedItems++;
            job.UpdatedAt = finding.ResolvedAt.Value;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task ThrowIfCancellationRequested(
        ServerDbContext db,
        StorageMaintenanceJob job,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await db.Entry(job).ReloadAsync(cancellationToken);
        if (job.Status is StorageMaintenanceJobStatus.CancelRequested)
        {
            throw new OperationCanceledException("Maintenance job cancellation requested.");
        }
    }
}
