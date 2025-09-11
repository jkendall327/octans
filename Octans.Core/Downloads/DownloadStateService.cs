using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Octans.Core.Downloaders;
using Octans.Core.Models;
using System.Collections.Concurrent;

namespace Octans.Core.Downloads;

public class DownloadStatusChangedEventArgs : EventArgs
{
    public required DownloadStatus Status { get; init; }
}

public class DownloadsChangedEventArgs : EventArgs
{
    public Guid? AffectedDownloadId { get; init; }
    public DownloadChangeType ChangeType { get; init; }
}

public enum DownloadChangeType
{
    Added,
    Updated,
    Removed
}

public interface IDownloadStateService
{
    event EventHandler<DownloadStatusChangedEventArgs>? OnDownloadProgressChanged;
    event EventHandler<DownloadsChangedEventArgs>? OnDownloadsChanged;
    Task InitializeFromDbAsync();
    IReadOnlyList<DownloadStatus> GetAllDownloads();
    DownloadStatus? GetDownloadById(Guid id);
    void UpdateProgress(Guid id, long bytesDownloaded, long totalBytes, double speed);
    void UpdateState(Guid id, DownloadState newState, string? errorMessage = null);
    Task AddOrUpdateDownloadAsync(DownloadStatus status);
    Task RemoveDownloadAsync(Guid id);
}

public class DownloadStateService(
    ILogger<DownloadStateService> logger,
    IDbContextFactory<ServerDbContext> contextFactory) : IDownloadStateService
{
    private readonly ConcurrentDictionary<Guid, DownloadStatus> _activeDownloads = new();

    public event EventHandler<DownloadStatusChangedEventArgs>? OnDownloadProgressChanged;
    public event EventHandler<DownloadsChangedEventArgs>? OnDownloadsChanged;

    public async Task InitializeFromDbAsync()
    {
        await using var db = await contextFactory.CreateDbContextAsync();

        var statuses = await db.DownloadStatuses
            .Where(d => d.State != DownloadState.Completed && d.State != DownloadState.Canceled)
            .ToListAsync();

        foreach (var status in statuses)
        {
            _activeDownloads[status.Id] = status;
        }

        OnDownloadsChanged?.Invoke(this, new()
        {
            ChangeType = DownloadChangeType.Updated
        });
    }

    public IReadOnlyList<DownloadStatus> GetAllDownloads()
    {
        return _activeDownloads.Values.OrderByDescending(d => d.CreatedAt).ToList();
    }

    public DownloadStatus? GetDownloadById(Guid id)
    {
        return _activeDownloads.TryGetValue(id, out var status) ? status : null;
    }

    public void UpdateProgress(Guid id, long bytesDownloaded, long totalBytes, double speed)
    {
        if (!_activeDownloads.TryGetValue(id, out var status)) return;

        status.BytesDownloaded = bytesDownloaded;
        status.TotalBytes = totalBytes;
        status.CurrentSpeed = speed;
        status.LastUpdated = DateTime.UtcNow;

        // Notify subscribers
        OnDownloadProgressChanged?.Invoke(this, new() { Status = status });
    }

    public void UpdateState(Guid id, DownloadState newState, string? errorMessage = null)
    {
        if (!_activeDownloads.TryGetValue(id, out var status)) return;

        status.State = newState;
        status.LastUpdated = DateTime.UtcNow;

        switch (newState)
        {
            case DownloadState.InProgress:
                status.StartedAt ??= DateTime.UtcNow;
                break;
            case DownloadState.Completed:
                status.CompletedAt = DateTime.UtcNow;
                break;
            case DownloadState.Failed:
                status.ErrorMessage = errorMessage;
                break;
        }

        // Persist state change to database
        Task.Run(async () =>
        {
            await using var db = await contextFactory.CreateDbContextAsync();

            await using var scope = await db.Database.BeginTransactionAsync();

            try
            {
                var dbStatus = await db.DownloadStatuses.FindAsync(id);
                if (dbStatus != null)
                {
                    dbStatus.State = status.State;
                    dbStatus.BytesDownloaded = status.BytesDownloaded;
                    dbStatus.TotalBytes = status.TotalBytes;
                    dbStatus.LastUpdated = status.LastUpdated;
                    dbStatus.StartedAt = status.StartedAt;
                    dbStatus.CompletedAt = status.CompletedAt;
                    dbStatus.ErrorMessage = status.ErrorMessage;

                    await db.SaveChangesAsync();
                    await scope.CommitAsync();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to persist download state change");
            }
        });

        // Notify subscribers
        OnDownloadProgressChanged?.Invoke(this, new() { Status = status });
        OnDownloadsChanged?.Invoke(this, new()
        {
            AffectedDownloadId = id,
            ChangeType = DownloadChangeType.Updated
        });
    }

    public async Task AddOrUpdateDownloadAsync(DownloadStatus status)
    {
        _activeDownloads[status.Id] = status;

        // Perform database operations
        try
        {
            await using var db = await contextFactory.CreateDbContextAsync();

            var existingStatus = await db.DownloadStatuses.FindAsync(status.Id);

            if (existingStatus == null)
            {
                db.DownloadStatuses.Add(status);
            }
            else
            {
                db.Entry(existingStatus).CurrentValues.SetValues(status);
            }

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist download status");
        }

        // Notification occurs after all operations are complete
        OnDownloadsChanged?.Invoke(this, new()
        {
            AffectedDownloadId = status.Id,
            ChangeType = DownloadChangeType.Added
        });
    }

    public async Task RemoveDownloadAsync(Guid id)
    {
        var removed = _activeDownloads.TryRemove(id, out _);

        if (!removed) return;

        try
        {
            await using var db = await contextFactory.CreateDbContextAsync();
            var status = await db.DownloadStatuses.FindAsync(id);

            if (status != null)
            {
                db.DownloadStatuses.Remove(status);
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to remove download status from database");
        }

        // Notify after everything is complete
        OnDownloadsChanged?.Invoke(this, new()
        {
            AffectedDownloadId = id,
            ChangeType = DownloadChangeType.Removed
        });
    }
}
