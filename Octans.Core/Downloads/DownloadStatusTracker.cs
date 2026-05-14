using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Octans.Core.Downloads.Models;
using Octans.Data.Models;

namespace Octans.Core.Downloads;

public interface IDownloadStateService
{
    event DownloadsChangedHandler? DownloadsChanged;
    event DownloadStatusChangedHandler? DownloadStatusChanged;

    Task InitializeFromDbAsync();
    IReadOnlyList<DownloadStatus> GetAllDownloads();
    DownloadStatus? GetDownloadById(Guid id);
    Task UpdateProgress(Guid id, long bytesDownloaded, long totalBytes, double speed);
    Task UpdateState(Guid id, DownloadState newState, string? errorMessage = null);
    Task AddOrUpdateDownloadAsync(DownloadStatus status);
    Task RemoveDownloadAsync(Guid id);
}

public delegate ValueTask DownloadsChangedHandler(DownloadsChanged notification);

public delegate ValueTask DownloadStatusChangedHandler(DownloadStatusChanged notification);

public class DownloadStatusTracker(
    IDbContextFactory<ServerDbContext> contextFactory,
    TimeProvider timeProvider,
    ILogger<DownloadStatusTracker> logger) : IDownloadStateService
{
    private readonly ConcurrentDictionary<Guid, DownloadStatus> _activeDownloads = new();

    public event DownloadsChangedHandler? DownloadsChanged;
    public event DownloadStatusChangedHandler? DownloadStatusChanged;

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

        await Raise(new DownloadsChanged
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

    public async Task UpdateProgress(Guid id, long bytesDownloaded, long totalBytes, double speed)
    {
        if (!_activeDownloads.TryGetValue(id, out var status)) return;

        status.BytesDownloaded = bytesDownloaded;
        status.TotalBytes = totalBytes;
        status.CurrentSpeed = speed;
        status.LastUpdated = timeProvider.GetUtcNow();

        await Raise(new DownloadStatusChanged { Status = status });
    }

    public async Task UpdateState(Guid id, DownloadState newState, string? errorMessage = null)
    {
        if (!_activeDownloads.TryGetValue(id, out var status)) return;

        var now = timeProvider.GetUtcNow();
        status.State = newState;
        status.LastUpdated = now;

        switch (newState)
        {
            case DownloadState.InProgress:
                status.StartedAt ??= now;
                break;
            case DownloadState.Completed:
                status.CompletedAt = now;
                break;
            case DownloadState.Failed:
                status.ErrorMessage = errorMessage;
                break;
        }

        // Persist state change to database
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

        await Raise(new DownloadStatusChanged { Status = status });

        await Raise(new DownloadsChanged
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

        await Raise(new DownloadsChanged
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

        await Raise(new DownloadsChanged
        {
            AffectedDownloadId = id,
            ChangeType = DownloadChangeType.Removed
        });
    }

    private async Task Raise(DownloadsChanged notification)
    {
        var handler = DownloadsChanged;
        if (handler is null) return;

        foreach (var @delegate in handler.GetInvocationList())
        {
            var subscriber = (DownloadsChangedHandler)@delegate;
            await subscriber(notification);
        }
    }

    private async Task Raise(DownloadStatusChanged notification)
    {
        var handler = DownloadStatusChanged;
        if (handler is null) return;

        foreach (var @delegate in handler.GetInvocationList())
        {
            var subscriber = (DownloadStatusChangedHandler)@delegate;
            await subscriber(notification);
        }
    }
}
