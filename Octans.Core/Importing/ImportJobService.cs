using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Octans.Core.Tags;
using Octans.Data.Models;
using DataImportItem = Octans.Data.Models.Importing.ImportItem;
using DataImportItemStatus = Octans.Data.Models.Importing.ImportItemStatus;
using DataImportJob = Octans.Data.Models.Importing.ImportJob;
using DataImportJobPhase = Octans.Data.Models.Importing.ImportJobPhase;
using DataImportJobStatus = Octans.Data.Models.Importing.ImportJobStatus;
using DataImportType = Octans.Data.Models.Importing.ImportType;

namespace Octans.Core.Importing;

public interface IImportJobNotifier
{
    Task JobChanged(Guid jobId, CancellationToken cancellationToken = default);
}

public sealed class NoOpImportJobNotifier : IImportJobNotifier
{
    public Task JobChanged(Guid jobId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public interface IImportJobService
{
    Task<ImportJobCreatedDto> Create(ImportJobCreateRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ImportJobDto>> GetJobs(CancellationToken cancellationToken = default);
    Task<ImportJobDto?> GetJob(Guid id, CancellationToken cancellationToken = default);
    Task<ImportJobDto?> PauseJob(Guid id, CancellationToken cancellationToken = default);
    Task<ImportJobDto?> ResumeJob(Guid id, CancellationToken cancellationToken = default);
    Task<ImportJobDto?> CancelJob(Guid id, CancellationToken cancellationToken = default);
}

public class ImportJobService(
    ServerDbContext context,
    TimeProvider timeProvider,
    IImportJobNotifier notifier,
    ILogger<ImportJobService> logger) : IImportJobService
{
    public async Task<ImportJobCreatedDto> Create(ImportJobCreateRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Sources.Count == 0)
        {
            throw new ArgumentException("At least one source is required.", nameof(request));
        }

        var now = Now();
        var job = new DataImportJob
        {
            Id = Guid.NewGuid(),
            Status = DataImportJobStatus.Queued,
            Phase = DataImportJobPhase.Scanning,
            TotalItems = request.Sources.Count,
            CreatedAt = now,
            UpdatedAt = now,
            DeleteAfterImport = request.DeleteAfterImport,
            AllowReimportDeleted = request.AllowReimportDeleted,
            AutoArchive = request.AutoArchive,
            SerializedFilterData = request.FilterData is null ? null : JsonSerializer.Serialize(request.FilterData)
        };

        var importRequest = request.ToImportRequest();
        job.SerializedRequest = JsonSerializer.Serialize(importRequest);

        foreach (var source in request.Sources)
        {
            var item = new DataImportItem
            {
                Id = Guid.NewGuid(),
                ImportJobId = job.Id,
                ImportType = MapImportType(request.ImportType),
                Source = source,
                SerializedTags = SerializeTags(request.TagsBySource, source),
                Status = DataImportItemStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now
            };

            job.Items.Add(item);
        }

        context.ImportJobs.Add(job);
        await context.SaveChangesAsync(cancellationToken);
        await notifier.JobChanged(job.Id, cancellationToken);

        logger.LogInformation("Created import job {JobId} with {ItemCount} items", job.Id, job.TotalItems);

        return new(job.Id);
    }

    public async Task<IReadOnlyList<ImportJobDto>> GetJobs(CancellationToken cancellationToken = default)
    {
        var jobs = await context.ImportJobs
            .Include(j => j.Items)
            .OrderByDescending(j => j.CreatedAt)
            .Take(25)
            .ToListAsync(cancellationToken);

        return jobs.Select(MapJob).ToList();
    }

    public async Task<ImportJobDto?> GetJob(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await context.ImportJobs
            .Include(j => j.Items)
            .SingleOrDefaultAsync(j => j.Id == id, cancellationToken);

        return job is null ? null : MapJob(job);
    }

    public Task<ImportJobDto?> PauseJob(Guid id, CancellationToken cancellationToken = default) =>
        Transition(id, DataImportJobStatus.Paused, DataImportJobStatus.PauseRequested, cancellationToken);

    public Task<ImportJobDto?> ResumeJob(Guid id, CancellationToken cancellationToken = default) =>
        Transition(id, DataImportJobStatus.Queued, DataImportJobStatus.Running, cancellationToken);

    public Task<ImportJobDto?> CancelJob(Guid id, CancellationToken cancellationToken = default) =>
        Transition(id, DataImportJobStatus.Cancelled, DataImportJobStatus.CancelRequested, cancellationToken);

    public ImportRequest BuildImportRequest(DataImportJob job, DataImportItem item)
    {
        var filterData = job.SerializedFilterData is null
            ? null
            : JsonSerializer.Deserialize<ImportFilterData>(job.SerializedFilterData);

        return new()
        {
            ImportId = job.Id,
            ImportType = MapImportType(item.ImportType),
            Items =
            [
                new()
                {
                    Filepath = item.ImportType is DataImportType.File ? item.Source : null,
                    Url = item.ImportType is DataImportType.RawUrl ? new Uri(item.Source) : null,
                    Tags = DeserializeTags(item.SerializedTags)
                }
            ],
            DeleteAfterImport = job.DeleteAfterImport,
            AllowReimportDeleted = job.AllowReimportDeleted,
            AutoArchive = job.AutoArchive,
            FilterData = filterData
        };
    }

    public ImportJobDto MapJob(DataImportJob job) => new(
        job.Id,
        job.Status.ToString(),
        job.Phase.ToString(),
        job.TotalItems,
        job.ProcessedItems,
        job.FailedItems,
        job.CurrentItem,
        job.FailureReason,
        job.CreatedAt,
        job.StartedAt,
        job.CompletedAt,
        job.UpdatedAt,
        job.Items.OrderBy(i => i.Id).Select(MapItem).ToList());

    private async Task<ImportJobDto?> Transition(
        Guid id,
        DataImportJobStatus idleTarget,
        DataImportJobStatus runningTarget,
        CancellationToken cancellationToken)
    {
        var job = await context.ImportJobs
            .Include(j => j.Items)
            .SingleOrDefaultAsync(j => j.Id == id, cancellationToken);

        if (job is null)
        {
            return null;
        }

        if (IsTerminal(job.Status))
        {
            return MapJob(job);
        }

        var target = job.Status switch
        {
            DataImportJobStatus.Queued or DataImportJobStatus.Paused => idleTarget,
            DataImportJobStatus.Running or DataImportJobStatus.PauseRequested => runningTarget,
            DataImportJobStatus.CancelRequested => DataImportJobStatus.CancelRequested,
            _ => job.Status
        };

        job.Status = target;
        job.UpdatedAt = Now();

        if (target is DataImportJobStatus.Cancelled)
        {
            foreach (var item in job.Items.Where(i => i.Status is DataImportItemStatus.Pending))
            {
                item.Status = DataImportItemStatus.Cancelled;
                item.UpdatedAt = job.UpdatedAt;
                item.CompletedAt = job.UpdatedAt;
            }

            job.CompletedAt = job.UpdatedAt;
        }

        await context.SaveChangesAsync(cancellationToken);
        await notifier.JobChanged(job.Id, cancellationToken);

        return MapJob(job);
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    private static ImportJobItemDto MapItem(DataImportItem item) => new(
        item.Id,
        item.ImportType.ToString(),
        item.Source,
        item.Status.ToString(),
        item.Error,
        item.Attempts,
        item.StartedAt,
        item.CompletedAt,
        item.UpdatedAt);

    private static bool IsTerminal(DataImportJobStatus status) =>
        status is DataImportJobStatus.Cancelled or DataImportJobStatus.Completed or DataImportJobStatus.Failed;

    private static DataImportType MapImportType(ImportType importType) =>
        importType switch
        {
            ImportType.File => DataImportType.File,
            ImportType.RawUrl => DataImportType.RawUrl,
            _ => throw new ArgumentOutOfRangeException(nameof(importType), importType, "Import type is not supported by import jobs.")
        };

    private static ImportType MapImportType(DataImportType importType) =>
        importType switch
        {
            DataImportType.File => ImportType.File,
            DataImportType.RawUrl => ImportType.RawUrl,
            _ => throw new ArgumentOutOfRangeException(nameof(importType), importType, "Import type is not supported by import jobs.")
        };

    private static string? SerializeTags(IReadOnlyDictionary<string, ICollection<TagModel>>? tagsBySource, string source)
    {
        if (tagsBySource is null || !tagsBySource.TryGetValue(source, out var tags) || tags.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(tags);
    }

    private static List<TagModel>? DeserializeTags(string? serializedTags) =>
        string.IsNullOrWhiteSpace(serializedTags)
            ? null
            : JsonSerializer.Deserialize<List<TagModel>>(serializedTags);
}

public record ImportJobCreateRequest
{
    public required ImportType ImportType { get; init; }
    public required List<string> Sources { get; init; }
    public bool DeleteAfterImport { get; init; }
    public bool AllowReimportDeleted { get; init; }
    public bool AutoArchive { get; init; }
    public ImportFilterData? FilterData { get; init; }
    public IReadOnlyDictionary<string, ICollection<TagModel>>? TagsBySource { get; init; }

    public ImportRequest ToImportRequest() => new()
    {
        ImportType = ImportType,
        Items = Sources.Select(source => new ImportItem
        {
            Filepath = ImportType is ImportType.File ? source : null,
            Url = ImportType is ImportType.RawUrl ? new Uri(source) : null,
            Tags = TagsBySource is not null && TagsBySource.TryGetValue(source, out var tags) ? tags : null
        }).ToList(),
        DeleteAfterImport = DeleteAfterImport,
        AllowReimportDeleted = AllowReimportDeleted,
        AutoArchive = AutoArchive,
        FilterData = FilterData
    };
}

public record ImportJobCreatedDto(Guid JobId);

public record ImportJobDto(
    Guid Id,
    string Status,
    string Phase,
    int TotalItems,
    int ProcessedItems,
    int FailedItems,
    string? CurrentItem,
    string? FailureReason,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    DateTime UpdatedAt,
    IReadOnlyList<ImportJobItemDto> Items);

public record ImportJobItemDto(
    Guid Id,
    string ImportType,
    string Source,
    string Status,
    string? Error,
    int Attempts,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    DateTime UpdatedAt);
