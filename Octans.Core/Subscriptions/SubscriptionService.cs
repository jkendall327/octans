using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Octans.Core.Importing;
using Octans.Core.Http;
using Octans.Core.Progress;
using Octans.Core.Tags;
using Octans.Data.Models;
using Octans.Data.Models.Importing;
using Octans.Data.Models.Subscriptions;
using CoreImportItem = Octans.Core.Importing.ImportItem;
using CoreImportType = Octans.Core.Importing.ImportType;
using DataImportItem = Octans.Data.Models.Importing.ImportItem;
using DataImportItemStatus = Octans.Data.Models.Importing.ImportItemStatus;
using DataImportType = Octans.Data.Models.Importing.ImportType;

namespace Octans.Core.Subscriptions;

public interface ISubscriptionService
{
    Task CheckAndExecute(CancellationToken stoppingToken = default);
    Task<List<SubscriptionStatusDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubscriptionExecutionDto>> GetHistoryAsync(
        int id,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubscriptionSourceItemDto>> GetSourceItemsAsync(
        int id,
        CancellationToken cancellationToken = default);
    Task RunNowAsync(int id, CancellationToken cancellationToken = default);
    Task SetEnabledAsync(int id, bool enabled, CancellationToken cancellationToken = default);
    Task AddAsync(
        string name,
        string downloaderName,
        string query,
        TimeSpan frequency,
        SubscriptionImportSettings? importSettings = null,
        IReadOnlyList<TagModel>? tags = null,
        int maxItemsPerRun = 100,
        CancellationToken cancellationToken = default);
    Task UpdateAsync(
        int id,
        string name,
        string downloaderName,
        string query,
        TimeSpan frequency,
        SubscriptionImportSettings? importSettings = null,
        IReadOnlyList<TagModel>? tags = null,
        int maxItemsPerRun = 100,
        CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}

internal sealed class SubscriptionService(
    IDbContextFactory<ServerDbContext> factory,
    TimeProvider timeProvider,
    IBackgroundProgressReporter reporter,
    ISubscriptionExecutor executor,
    ILogger<SubscriptionService> logger,
    IDownloadStateService? downloadStateService = null,
    System.IO.Abstractions.IFileSystem? fileSystem = null) : ISubscriptionService
{
    private static readonly SemaphoreSlim SchedulerLock = new(1, 1);

    public async Task CheckAndExecute(CancellationToken stoppingToken = default)
    {
        await SchedulerLock.WaitAsync(stoppingToken);

        try
        {
            await CheckAndExecuteCore(stoppingToken);
        }
        finally
        {
            SchedulerLock.Release();
        }
    }

    public async Task<List<SubscriptionStatusDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        var subscriptions = await db.Subscriptions
            .Include(s => s.Provider)
            .Include(s => s.Executions)
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);

        return subscriptions.Select(MapStatus).ToList();
    }

    public async Task<IReadOnlyList<SubscriptionExecutionDto>> GetHistoryAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var executions = await db.SubscriptionExecutions
            .Where(e => e.SubscriptionId == id)
            .OrderByDescending(e => e.ExecutedAt)
            .Take(100)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return executions.Select(e => new SubscriptionExecutionDto(
            e.Id,
            e.SubscriptionId,
            e.ExecutedAt,
            e.CompletedAt,
            e.Status,
            e.ItemsFound,
            e.ItemsQueued,
            e.ItemsSkipped,
            e.ImportJobId,
            e.ErrorMessage,
            e.Diagnostics)).ToList();
    }

    public async Task<IReadOnlyList<SubscriptionSourceItemDto>> GetSourceItemsAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var sourceItems = await db.SubscriptionSourceItems
            .Where(item => item.SubscriptionId == id)
            .OrderByDescending(item => item.LastSeenAt)
            .Take(500)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return sourceItems.Select(item => new SubscriptionSourceItemDto(
            item.Id,
            item.SourceId,
            item.RemoteUrl,
            item.FirstSeenAt,
            item.LastSeenAt,
            item.QueuedAt,
            item.ImportedAt,
            item.LastExecutionId)).ToList();
    }

    public async Task RunNowAsync(int id, CancellationToken cancellationToken = default)
    {
        await SchedulerLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            var subscription = await db.Subscriptions
                .Include(item => item.Provider)
                .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (subscription is null)
            {
                throw new KeyNotFoundException($"Subscription {id} was not found.");
            }

            if (subscription.IsRunning)
            {
                throw new InvalidOperationException($"Subscription {id} is already running.");
            }

            await ExecuteSubscriptionAsync(db, subscription, cancellationToken);
        }
        finally
        {
            SchedulerLock.Release();
        }
    }

    public async Task SetEnabledAsync(int id, bool enabled, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var subscription = await db.Subscriptions.FindAsync([id], cancellationToken);
        if (subscription is null)
        {
            throw new KeyNotFoundException($"Subscription {id} was not found.");
        }

        subscription.IsEnabled = enabled;
        if (enabled && subscription.NextCheck > timeProvider.GetUtcNow())
        {
            subscription.NextCheck = timeProvider.GetUtcNow();
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddAsync(
        string name,
        string downloaderName,
        string query,
        TimeSpan frequency,
        SubscriptionImportSettings? importSettings = null,
        IReadOnlyList<TagModel>? tags = null,
        int maxItemsPerRun = 100,
        CancellationToken cancellationToken = default)
    {
        ValidateConfiguration(name, downloaderName, query, frequency, maxItemsPerRun);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var provider = await GetOrCreateProviderAsync(db, downloaderName, cancellationToken);
        var settings = importSettings ?? SubscriptionImportSettings.Default;

        db.Subscriptions.Add(new Subscription
        {
            Name = name.Trim(),
            Provider = provider,
            Query = query.Trim(),
            CheckPeriod = frequency,
            NextCheck = timeProvider.GetUtcNow(),
            RepositoryId = (int)settings.Repository,
            AllowReimportDeleted = settings.AllowReimportDeleted,
            AutoArchive = settings.AutoArchive,
            SerializedTags = SerializeTags(tags),
            MaxItemsPerRun = maxItemsPerRun,
            IsEnabled = true
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(
        int id,
        string name,
        string downloaderName,
        string query,
        TimeSpan frequency,
        SubscriptionImportSettings? importSettings = null,
        IReadOnlyList<TagModel>? tags = null,
        int maxItemsPerRun = 100,
        CancellationToken cancellationToken = default)
    {
        ValidateConfiguration(name, downloaderName, query, frequency, maxItemsPerRun);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var subscription = await db.Subscriptions.FindAsync([id], cancellationToken);
        if (subscription is null)
        {
            throw new KeyNotFoundException($"Subscription {id} was not found.");
        }

        var provider = await GetOrCreateProviderAsync(db, downloaderName, cancellationToken);
        var settings = importSettings ?? SubscriptionImportSettings.Default;
        subscription.Name = name.Trim();
        subscription.Provider = provider;
        subscription.Query = query.Trim();
        subscription.CheckPeriod = frequency;
        subscription.RepositoryId = (int)settings.Repository;
        subscription.AllowReimportDeleted = settings.AllowReimportDeleted;
        subscription.AutoArchive = settings.AutoArchive;
        subscription.SerializedTags = SerializeTags(tags);
        subscription.MaxItemsPerRun = maxItemsPerRun;
        if (!subscription.IsRunning)
        {
            subscription.NextCheck = timeProvider.GetUtcNow();
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var subscription = await db.Subscriptions.FindAsync([id], cancellationToken);
        if (subscription is null)
        {
            return;
        }

        db.Subscriptions.Remove(subscription);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task CheckAndExecuteCore(CancellationToken stoppingToken)
    {
        await using var db = await factory.CreateDbContextAsync(stoppingToken);
        var now = timeProvider.GetUtcNow();
        await RecoverStaleRunsAsync(db, now, stoppingToken);
        var subscriptions = await db.Subscriptions
            .Include(s => s.Provider)
            .Where(s => s.IsEnabled && !s.IsRunning && s.NextCheck <= now)
            .OrderBy(s => s.NextCheck)
            .ThenBy(s => s.Id)
            .ToListAsync(stoppingToken);

        await reporter.ReportMessage($"Executing {subscriptions.Count} subscriptions...");

        foreach (var subscription in subscriptions)
        {
            stoppingToken.ThrowIfCancellationRequested();
            await ExecuteSubscriptionAsync(db, subscription, stoppingToken);
        }
    }

    private async Task ExecuteSubscriptionAsync(
        ServerDbContext db,
        Subscription subscription,
        CancellationToken cancellationToken)
    {
        var execution = await StartExecutionAsync(db, subscription, cancellationToken);
        var queuedDownloadIds = new List<Guid>();
        try
        {
            var result = await executor.ExecuteAsync(subscription, cancellationToken);
            execution.ItemsFound = result.ItemsFound;
            execution.Diagnostics = result.Diagnostics;
            subscription.Cursor = result.NextCursor;

            var newItems = await FindAndRememberNewItemsAsync(
                db,
                subscription,
                execution,
                result.DiscoveredItems,
                cancellationToken);
            execution.ItemsSkipped = result.ItemsFound - newItems.Count;

            if (newItems.Count > 0)
            {
                var completedAt = timeProvider.GetUtcNow();
                var importJob = CreateImportJob(db, subscription, execution, newItems, completedAt);
                db.ImportJobs.Add(importJob);
                execution.ImportJobId = importJob.Id;
                execution.ItemsQueued = newItems.Count;
                queuedDownloadIds.AddRange(importJob.Items
                    .Where(item => item.DownloadId is not null)
                    .Select(item => item.DownloadId!.Value));
                foreach (var item in newItems)
                {
                    item.QueuedAt = completedAt;
                }
            }

            CompleteSubscription(subscription, execution, timeProvider.GetUtcNow(), succeeded: true);
            logger.LogInformation(
                "Executed subscription {SubscriptionId} ({Name}); found {ItemsFound}, queued {ItemsQueued}, skipped {ItemsSkipped}",
                subscription.Id,
                subscription.Name,
                execution.ItemsFound,
                execution.ItemsQueued,
                execution.ItemsSkipped);
        }
        catch (OperationCanceledException)
        {
            execution.Status = SubscriptionExecutionStatus.Cancelled;
            execution.ErrorMessage = "Subscription execution was cancelled.";
            CompleteSubscription(subscription, execution, timeProvider.GetUtcNow(), succeeded: false);
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            execution.Status = SubscriptionExecutionStatus.Failed;
            execution.ErrorMessage = Truncate(ex.Message, 2000);
            execution.Diagnostics = Truncate(ex.ToString(), 2000);
            CompleteSubscription(subscription, execution, timeProvider.GetUtcNow(), succeeded: false);
            logger.LogError(ex, "Subscription {SubscriptionId} ({Name}) failed", subscription.Id, subscription.Name);
        }

        await db.SaveChangesAsync(cancellationToken);
        if (downloadStateService is null)
        {
            return;
        }

        foreach (var downloadId in queuedDownloadIds)
        {
            var status = await db.DownloadStatuses
                .AsNoTracking()
                .SingleAsync(download => download.Id == downloadId, cancellationToken);
            await downloadStateService.AddOrUpdateDownloadAsync(status);
        }
    }

    private async Task RecoverStaleRunsAsync(
        ServerDbContext db,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var staleBefore = now - TimeSpan.FromHours(6);
        var staleSubscriptions = await db.Subscriptions
            .Where(subscription => subscription.IsRunning
                                   && (subscription.LastStartedAt == null || subscription.LastStartedAt < staleBefore))
            .ToListAsync(cancellationToken);
        if (staleSubscriptions.Count == 0)
        {
            return;
        }

        var staleSubscriptionIds = staleSubscriptions.Select(subscription => subscription.Id).ToList();
        var staleExecutions = await db.SubscriptionExecutions
            .Where(execution => staleSubscriptionIds.Contains(execution.SubscriptionId)
                                && execution.Status == SubscriptionExecutionStatus.Running)
            .ToListAsync(cancellationToken);

        foreach (var subscription in staleSubscriptions)
        {
            subscription.IsRunning = false;
            subscription.NextCheck = now;
            subscription.LastError = "Recovered a subscription run left active by a previous process.";
            subscription.ConsecutiveFailures++;
        }

        foreach (var execution in staleExecutions)
        {
            execution.Status = SubscriptionExecutionStatus.Failed;
            execution.CompletedAt = now;
            execution.ErrorMessage = "Subscription run was recovered after the previous process stopped.";
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<SubscriptionExecution> StartExecutionAsync(
        ServerDbContext db,
        Subscription subscription,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        subscription.IsRunning = true;
        subscription.LastStartedAt = now;
        subscription.LastError = null;
        var execution = new SubscriptionExecution
        {
            SubscriptionId = subscription.Id,
            ExecutedAt = now,
            Status = SubscriptionExecutionStatus.Running
        };
        db.SubscriptionExecutions.Add(execution);
        await db.SaveChangesAsync(cancellationToken);
        return execution;
    }

    private async Task<List<SubscriptionSourceItem>> FindAndRememberNewItemsAsync(
        ServerDbContext db,
        Subscription subscription,
        SubscriptionExecution execution,
        IReadOnlyList<SubscriptionDiscoveredItem> discovered,
        CancellationToken cancellationToken)
    {
        var distinct = discovered
            .Where(item => !string.IsNullOrWhiteSpace(item.SourceId))
            .Select(item => new
            {
                SourceId = Truncate(item.SourceId.Trim(), 500),
                RemoteUrl = NormalizeUrl(item.RemoteUrl)
            })
            .DistinctBy(item => $"{item.SourceId}|{item.RemoteUrl}", StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (distinct.Count == 0)
        {
            return [];
        }

        var sourceIds = distinct.Select(item => item.SourceId).ToList();
        var existing = await db.SubscriptionSourceItems
            .Where(item => item.SubscriptionId == subscription.Id && sourceIds.Contains(item.SourceId))
            .ToListAsync(cancellationToken);
        var existingKeys = existing
            .Select(item => $"{item.SourceId}|{NormalizeUrl(item.RemoteUrl)}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var now = timeProvider.GetUtcNow();
        var fresh = new List<SubscriptionSourceItem>();
        foreach (var item in distinct)
        {
            var key = $"{item.SourceId}|{item.RemoteUrl}";
            if (existingKeys.Contains(key))
            {
                var seen = existing.Single(existingItem =>
                    string.Equals(existingItem.SourceId, item.SourceId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(NormalizeUrl(existingItem.RemoteUrl), item.RemoteUrl, StringComparison.OrdinalIgnoreCase));
                seen.LastSeenAt = now;
                seen.LastExecutionId = execution.Id;
                continue;
            }

            var source = new SubscriptionSourceItem
            {
                SubscriptionId = subscription.Id,
                FirstExecutionId = execution.Id,
                LastExecutionId = execution.Id,
                SourceId = item.SourceId,
                RemoteUrl = item.RemoteUrl,
                FirstSeenAt = now,
                LastSeenAt = now
            };
            db.SubscriptionSourceItems.Add(source);
            fresh.Add(source);
        }

        return fresh;
    }

    private ImportJob CreateImportJob(
        ServerDbContext db,
        Subscription subscription,
        SubscriptionExecution execution,
        List<SubscriptionSourceItem> sourceItems,
        DateTimeOffset now)
    {
        var tags = DeserializeTags(subscription.SerializedTags);
        var downloadIds = sourceItems.ToDictionary(item => item, _ => Guid.NewGuid());
        foreach (var sourceItem in sourceItems)
        {
            var uri = new Uri(sourceItem.RemoteUrl);
            var filename = Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrWhiteSpace(filename))
            {
                filename = "download";
            }

            var downloadId = downloadIds[sourceItem];
            var destination = Path.Combine(
                fileSystem?.Path.GetTempPath() ?? "/tmp",
                "octans-subscriptions",
                subscription.Id.ToString(CultureInfo.InvariantCulture),
                execution.Id.ToString(CultureInfo.InvariantCulture),
                $"{downloadId}-{filename}");
            db.DownloadStatuses.Add(new DownloadStatus
            {
                Id = downloadId,
                Url = sourceItem.RemoteUrl,
                Filename = filename,
                DestinationPath = destination,
                State = DownloadState.Queued,
                CreatedAt = now,
                LastUpdated = now,
                Domain = uri.Host,
                SourceType = "Subscription",
                SourceId = sourceItem.SourceId
            });
            db.QueuedDownloads.Add(new QueuedDownload
            {
                Id = downloadId,
                Url = sourceItem.RemoteUrl,
                DestinationPath = destination,
                QueuedAt = now,
                Domain = uri.Host,
                SourceType = "Subscription",
                SourceId = sourceItem.SourceId
            });
        }

        var importRequest = new ImportRequest
        {
            ImportId = Guid.NewGuid(),
            ImportType = CoreImportType.RawUrl,
            Items = sourceItems.Select(item => new CoreImportItem
            {
                Url = new Uri(item.RemoteUrl),
                SourceId = item.SourceId,
                SourceType = "Subscription",
                DownloadId = downloadIds[item],
                Tags = tags
            }).ToList(),
            DeleteAfterImport = false,
            AllowReimportDeleted = subscription.AllowReimportDeleted,
            AutoArchive = subscription.AutoArchive,
            Repository = (RepositoryType)subscription.RepositoryId
        };
        var job = new ImportJob
        {
            Id = importRequest.ImportId,
            Status = ImportJobStatus.Queued,
            Phase = ImportJobPhase.Scanning,
            TotalItems = sourceItems.Count,
            CreatedAt = now,
            UpdatedAt = now,
            AllowReimportDeleted = subscription.AllowReimportDeleted,
            AutoArchive = subscription.AutoArchive,
            RepositoryId = subscription.RepositoryId,
            SourceType = "Subscription",
            SourceId = $"subscription:{subscription.Id}:execution:{execution.Id}",
            SubscriptionId = subscription.Id,
            SubscriptionExecutionId = execution.Id,
            SerializedRequest = JsonSerializer.Serialize(importRequest)
        };

        foreach (var sourceItem in sourceItems)
        {
            job.Items.Add(new DataImportItem
            {
                Id = Guid.NewGuid(),
                ImportJobId = job.Id,
                ImportType = DataImportType.RawUrl,
                Source = sourceItem.RemoteUrl,
                SourceId = sourceItem.SourceId,
                SourceType = "Subscription",
                DownloadId = downloadIds[sourceItem],
                SerializedTags = tags is null ? null : JsonSerializer.Serialize(tags),
                Status = DataImportItemStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        return job;
    }

    private static void CompleteSubscription(
        Subscription subscription,
        SubscriptionExecution execution,
        DateTimeOffset completedAt,
        bool succeeded)
    {
        execution.CompletedAt = completedAt;
        if (succeeded)
        {
            execution.Status = SubscriptionExecutionStatus.Succeeded;
            subscription.ConsecutiveFailures = 0;
            subscription.LastError = null;
        }
        else
        {
            subscription.ConsecutiveFailures++;
            subscription.LastError = execution.ErrorMessage;
        }

        subscription.LastCompletedAt = completedAt;
        subscription.IsRunning = false;
        subscription.NextCheck = completedAt.Add(
            succeeded ? subscription.CheckPeriod : GetFailureDelay(subscription));
    }

    private static TimeSpan GetFailureDelay(Subscription subscription)
    {
        var multiplier = Math.Min(1L << Math.Min(subscription.ConsecutiveFailures - 1, 5), 32L);
        var ticks = (long)Math.Min(
            subscription.CheckPeriod.Ticks * (double)multiplier,
            TimeSpan.FromDays(1).Ticks);
        return TimeSpan.FromTicks(Math.Max(subscription.CheckPeriod.Ticks, ticks));
    }

    private static SubscriptionStatusDto MapStatus(Subscription subscription)
    {
        var lastExecution = subscription.Executions.OrderByDescending(e => e.ExecutedAt).FirstOrDefault();
        return new(
            subscription.Id,
            subscription.Name,
            subscription.Provider.Name,
            subscription.Query,
            subscription.CheckPeriod,
            lastExecution?.ExecutedAt,
            lastExecution?.Status,
            lastExecution?.ItemsFound,
            lastExecution?.ErrorMessage,
            subscription.NextCheck,
            subscription.IsEnabled,
            subscription.IsRunning,
            subscription.LastCompletedAt,
            subscription.LastError,
            subscription.ConsecutiveFailures,
            lastExecution?.ItemsQueued,
            lastExecution?.ItemsSkipped,
            subscription.MaxItemsPerRun);
    }

    private static async Task<Provider> GetOrCreateProviderAsync(
        ServerDbContext db,
        string downloaderName,
        CancellationToken cancellationToken)
    {
        var normalizedName = downloaderName.Trim();
        var provider = await db.Providers.FirstOrDefaultAsync(
            p => p.Name == normalizedName,
            cancellationToken);
        if (provider is not null)
        {
            return provider;
        }

        provider = new Provider { Name = normalizedName };
        db.Providers.Add(provider);
        return provider;
    }

    private static void ValidateConfiguration(
        string name,
        string downloaderName,
        string query,
        TimeSpan frequency,
        int maxItemsPerRun)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            throw new ArgumentException("Subscription name must be between 1 and 100 characters.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(downloaderName))
        {
            throw new ArgumentException("Downloader name is required.", nameof(downloaderName));
        }

        if (string.IsNullOrWhiteSpace(query) || query.Length > 500)
        {
            throw new ArgumentException("Subscription query must be between 1 and 500 characters.", nameof(query));
        }

        if (frequency < TimeSpan.FromMinutes(1))
        {
            throw new ArgumentException("Subscription frequency must be at least one minute.", nameof(frequency));
        }

        if (maxItemsPerRun is < 1 or > 10_000)
        {
            throw new ArgumentException("Maximum items per run must be between 1 and 10,000.", nameof(maxItemsPerRun));
        }
    }

    private static string NormalizeUrl(Uri uri) => uri.GetComponents(
        UriComponents.SchemeAndServer | UriComponents.PathAndQuery,
        UriFormat.UriEscaped).TrimEnd('/');

    private static string NormalizeUrl(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ? NormalizeUrl(parsed) : uri.TrimEnd('/');

    private static List<TagModel>? DeserializeTags(string? serializedTags) =>
        string.IsNullOrWhiteSpace(serializedTags)
            ? null
            : JsonSerializer.Deserialize<List<TagModel>>(serializedTags);

    private static string? SerializeTags(IReadOnlyList<TagModel>? tags) =>
        tags is { Count: > 0 } ? JsonSerializer.Serialize(tags) : null;

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}

public sealed record SubscriptionImportSettings(
    RepositoryType Repository,
    bool AllowReimportDeleted,
    bool AutoArchive)
{
    public static SubscriptionImportSettings Default { get; } = new(RepositoryType.Inbox, false, false);
}
