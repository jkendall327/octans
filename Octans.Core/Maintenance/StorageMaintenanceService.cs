using Microsoft.EntityFrameworkCore;
using Octans.Data.Models;
using Octans.Data.Models.Maintenance;

namespace Octans.Core.Maintenance;

public interface IStorageMaintenanceService
{
    Task<StorageMaintenanceJobCreated> QueueScanAsync(
        StorageMaintenanceTrigger trigger = StorageMaintenanceTrigger.Manual,
        CancellationToken cancellationToken = default);
    Task<StorageMaintenanceJobCreated> QueueRepairAsync(
        Guid scanJobId,
        StorageRepairActions actions,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StorageMaintenanceJobDto>> GetJobsAsync(CancellationToken cancellationToken = default);
    Task<StorageMaintenanceJobDto?> GetJobAsync(Guid id, CancellationToken cancellationToken = default);
    Task<StorageMaintenanceFindingsPage?> GetFindingsAsync(
        Guid scanJobId,
        StorageFindingResolution? resolution = null,
        StorageFindingType? type = null,
        int skip = 0,
        int take = 200,
        CancellationToken cancellationToken = default);
    Task<StorageMaintenanceJobDto?> CancelAsync(Guid id, CancellationToken cancellationToken = default);
}

internal sealed class StorageMaintenanceService(
    IDbContextFactory<ServerDbContext> factory,
    TimeProvider timeProvider) : IStorageMaintenanceService
{
    public async Task<StorageMaintenanceJobCreated> QueueScanAsync(
        StorageMaintenanceTrigger trigger = StorageMaintenanceTrigger.Manual,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var existing = await db.StorageMaintenanceJobs
            .Where(j => j.Type == StorageMaintenanceJobType.Scan)
            .Where(j => j.Status == StorageMaintenanceJobStatus.Queued ||
                        j.Status == StorageMaintenanceJobStatus.Running ||
                        j.Status == StorageMaintenanceJobStatus.CancelRequested)
            .OrderBy(j => j.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            return new(existing.Id);
        }

        var job = CreateJob(StorageMaintenanceJobType.Scan, trigger);
        db.StorageMaintenanceJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);

        return new(job.Id);
    }

    public async Task<StorageMaintenanceJobCreated> QueueRepairAsync(
        Guid scanJobId,
        StorageRepairActions actions,
        CancellationToken cancellationToken = default)
    {
        if (actions is StorageRepairActions.None || (actions & ~StorageRepairActions.AllSafe) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(actions), "At least one supported repair action is required.");
        }

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var scan = await db.StorageMaintenanceJobs.SingleOrDefaultAsync(j => j.Id == scanJobId, cancellationToken);
        if (scan is null || scan.Type is not StorageMaintenanceJobType.Scan)
        {
            throw new ArgumentException("The source scan does not exist.", nameof(scanJobId));
        }

        if (scan.Status is not StorageMaintenanceJobStatus.Completed)
        {
            throw new InvalidOperationException("Repairs can only be queued for a completed scan.");
        }

        var existing = await db.StorageMaintenanceJobs
            .Where(j => j.Type == StorageMaintenanceJobType.Repair && j.SourceScanJobId == scanJobId)
            .Where(j => j.Status == StorageMaintenanceJobStatus.Queued ||
                        j.Status == StorageMaintenanceJobStatus.Running ||
                        j.Status == StorageMaintenanceJobStatus.CancelRequested)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            return new(existing.Id);
        }

        var job = CreateJob(StorageMaintenanceJobType.Repair, StorageMaintenanceTrigger.Manual);
        job.SourceScanJobId = scanJobId;
        job.RepairActions = actions;
        db.StorageMaintenanceJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);

        return new(job.Id);
    }

    public async Task<IReadOnlyList<StorageMaintenanceJobDto>> GetJobsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var jobs = await db.StorageMaintenanceJobs
            .AsNoTracking()
            .OrderByDescending(j => j.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        return jobs.Select(Map).ToList();
    }

    public async Task<StorageMaintenanceJobDto?> GetJobAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var job = await db.StorageMaintenanceJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == id, cancellationToken);
        return job is null ? null : Map(job);
    }

    public async Task<StorageMaintenanceFindingsPage?> GetFindingsAsync(
        Guid scanJobId,
        StorageFindingResolution? resolution = null,
        StorageFindingType? type = null,
        int skip = 0,
        int take = 200,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        if (!await db.StorageMaintenanceJobs.AnyAsync(
                j => j.Id == scanJobId && j.Type == StorageMaintenanceJobType.Scan,
                cancellationToken))
        {
            return null;
        }

        var query = db.StorageMaintenanceFindings.AsNoTracking().Where(f => f.ScanJobId == scanJobId);
        if (resolution is not null)
        {
            query = query.Where(f => f.Resolution == resolution);
        }

        if (type is not null)
        {
            query = query.Where(f => f.Type == type);
        }

        var total = await query.CountAsync(cancellationToken);
        var findings = await query
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.Type)
            .ThenBy(f => f.Path)
            .Skip(Math.Max(0, skip))
            .Take(Math.Clamp(take, 1, 1000))
            .ToListAsync(cancellationToken);

        return new(total, findings.Select(Map).ToList());
    }

    public async Task<StorageMaintenanceJobDto?> CancelAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var job = await db.StorageMaintenanceJobs.SingleOrDefaultAsync(j => j.Id == id, cancellationToken);
        if (job is null)
        {
            return null;
        }

        if (job.Status is StorageMaintenanceJobStatus.Queued)
        {
            job.Status = StorageMaintenanceJobStatus.Cancelled;
            job.CompletedAt = Now();
        }
        else if (job.Status is StorageMaintenanceJobStatus.Running)
        {
            job.Status = StorageMaintenanceJobStatus.CancelRequested;
        }

        job.UpdatedAt = Now();
        await db.SaveChangesAsync(cancellationToken);
        return Map(job);
    }

    private StorageMaintenanceJob CreateJob(StorageMaintenanceJobType type, StorageMaintenanceTrigger trigger)
    {
        var now = Now();
        return new()
        {
            Id = Guid.NewGuid(),
            Type = type,
            Status = StorageMaintenanceJobStatus.Queued,
            Trigger = trigger,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private DateTimeOffset Now() => timeProvider.GetUtcNow();

    internal static StorageMaintenanceJobDto Map(StorageMaintenanceJob job) => new(
        job.Id, job.Type, job.Status, job.Trigger, job.RepairActions, job.SourceScanJobId,
        job.TotalItems, job.ProcessedItems, job.FindingsCount, job.RepairedItems, job.FailedItems,
        job.ScannedBytes, job.CurrentItem, job.FailureReason, job.CreatedAt, job.StartedAt,
        job.CompletedAt, job.UpdatedAt);

    private static StorageMaintenanceFindingDto Map(StorageMaintenanceFinding finding) => new(
        finding.Id, finding.ScanJobId, finding.Type, finding.Severity, finding.Hash, finding.Path,
        finding.ExpectedPath, finding.Size, finding.Message, finding.Resolution, finding.RepairJobId,
        finding.ResolvedAt, finding.ResolutionMessage);
}
