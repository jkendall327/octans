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
    Task<bool> TryUpdateState(
        Guid id,
        IReadOnlySet<DownloadState> expectedStates,
        DownloadState newState,
        string? errorMessage = null);
    Task QueueDownloadAsync(DownloadStatus status);
    Task<bool> TryRequeuePausedDownloadAsync(Guid id);
    Task<bool> TryRequeueFailedOrCanceledDownloadAsync(Guid id);
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

        ApplyState(status, newState, timeProvider.GetUtcNow(), errorMessage);

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

    public async Task<bool> TryUpdateState(
        Guid id,
        IReadOnlySet<DownloadState> expectedStates,
        DownloadState newState,
        string? errorMessage = null)
    {
        if (!_activeDownloads.TryGetValue(id, out var status) || !expectedStates.Contains(status.State))
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        var updatedStatus = CopyStatus(status);
        ApplyState(updatedStatus, newState, now, errorMessage);

        await using (var db = await contextFactory.CreateDbContextAsync())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();

            var dbStatus = await db.DownloadStatuses.FindAsync(id);
            if (dbStatus is null || !expectedStates.Contains(dbStatus.State))
            {
                return false;
            }

            ApplyPersistedState(dbStatus, updatedStatus);

            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        _activeDownloads[id] = updatedStatus;

        await Raise(new DownloadStatusChanged { Status = updatedStatus });
        await Raise(new DownloadsChanged
        {
            AffectedDownloadId = id,
            ChangeType = DownloadChangeType.Updated
        });

        return true;
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

    public async Task QueueDownloadAsync(DownloadStatus status)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            var existingStatus = await db.DownloadStatuses.FindAsync(status.Id);
            if (existingStatus == null)
            {
                db.DownloadStatuses.Add(status);
            }
            else
            {
                db.Entry(existingStatus).CurrentValues.SetValues(status);
            }

            await UpsertQueuedDownloadAsync(db, status, status.LastUpdated);

            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist queued download");
            throw;
        }

        _activeDownloads[status.Id] = status;

        await Raise(new DownloadsChanged
        {
            AffectedDownloadId = status.Id,
            ChangeType = DownloadChangeType.Added
        });
    }

    public Task<bool> TryRequeuePausedDownloadAsync(Guid id)
    {
        return TryRequeueExistingDownloadAsync(
            id,
            static status => status.State == DownloadState.Paused,
            static _ => { });
    }

    public Task<bool> TryRequeueFailedOrCanceledDownloadAsync(Guid id)
    {
        return TryRequeueExistingDownloadAsync(
            id,
            static status => status.State is DownloadState.Failed or DownloadState.Canceled,
            static status =>
            {
                status.BytesDownloaded = 0;
                status.CurrentSpeed = 0;
                status.ErrorMessage = null;
                status.StartedAt = null;
                status.CompletedAt = null;
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

    private async Task<bool> TryRequeueExistingDownloadAsync(
        Guid id,
        Func<DownloadStatus, bool> canRequeue,
        Action<DownloadStatus> prepareForQueue)
    {
        DownloadStatus? status;
        await using (var db = await contextFactory.CreateDbContextAsync())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();

            status = await db.DownloadStatuses.FindAsync(id);
            if (status is null || !canRequeue(status))
            {
                return false;
            }

            var now = timeProvider.GetUtcNow();
            prepareForQueue(status);
            ApplyState(status, DownloadState.Queued, now);
            await UpsertQueuedDownloadAsync(db, status, now);

            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        _activeDownloads[id] = status;

        await Raise(new DownloadStatusChanged { Status = status });
        await Raise(new DownloadsChanged
        {
            AffectedDownloadId = id,
            ChangeType = DownloadChangeType.Updated
        });

        return true;
    }

    private static void ApplyState(DownloadStatus status, DownloadState newState, DateTimeOffset now, string? errorMessage = null)
    {
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
    }

    private static DownloadStatus CopyStatus(DownloadStatus status) => new()
    {
        Id = status.Id,
        Url = status.Url,
        Filename = status.Filename,
        DisplayName = status.DisplayName,
        DestinationPath = status.DestinationPath,
        Priority = status.Priority,
        TotalBytes = status.TotalBytes,
        BytesDownloaded = status.BytesDownloaded,
        CurrentSpeed = status.CurrentSpeed,
        State = status.State,
        CreatedAt = status.CreatedAt,
        StartedAt = status.StartedAt,
        CompletedAt = status.CompletedAt,
        LastUpdated = status.LastUpdated,
        ErrorMessage = status.ErrorMessage,
        Domain = status.Domain,
        SourceType = status.SourceType,
        SourceId = status.SourceId
    };

    private static void ApplyPersistedState(DownloadStatus target, DownloadStatus source)
    {
        target.State = source.State;
        target.BytesDownloaded = source.BytesDownloaded;
        target.TotalBytes = source.TotalBytes;
        target.LastUpdated = source.LastUpdated;
        target.StartedAt = source.StartedAt;
        target.CompletedAt = source.CompletedAt;
        target.ErrorMessage = source.ErrorMessage;
    }

    private static async Task UpsertQueuedDownloadAsync(ServerDbContext db, DownloadStatus status, DateTimeOffset queuedAt)
    {
        var queuedDownload = db.QueuedDownloads.Local.FirstOrDefault(d => d.Id == status.Id)
                             ?? await db.QueuedDownloads.FindAsync(status.Id);

        if (queuedDownload is null)
        {
            db.QueuedDownloads.Add(BuildQueuedDownload(status, queuedAt));
            return;
        }

        queuedDownload.Url = status.Url;
        queuedDownload.DestinationPath = status.DestinationPath;
        queuedDownload.DisplayName = status.DisplayName;
        queuedDownload.QueuedAt = queuedAt;
        queuedDownload.Priority = status.Priority;
        queuedDownload.Domain = status.Domain;
        queuedDownload.SourceType = status.SourceType;
        queuedDownload.SourceId = status.SourceId;
    }

    private static QueuedDownload BuildQueuedDownload(DownloadStatus status, DateTimeOffset queuedAt) => new()
    {
        Id = status.Id,
        Url = status.Url,
        DestinationPath = status.DestinationPath,
        DisplayName = status.DisplayName,
        QueuedAt = queuedAt,
        Priority = status.Priority,
        Domain = status.Domain,
        SourceType = status.SourceType,
        SourceId = status.SourceId
    };
}
