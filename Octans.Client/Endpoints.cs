using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using Octans.Core;
using Octans.Core.Duplicates;
using Octans.Core.Downloaders;
using Octans.Core.Filesystem;
using Octans.Core.Http;
using Octans.Core.Http.Models;
using Octans.Core.Importing;
using Octans.Core.Maintenance;
using Octans.Core.Notes;
using Octans.Core.Querying;
using Octans.Core.Repositories;
using Octans.Core.Stats;
using Octans.Core.Subscriptions;
using Octans.Core.Tags;
using Octans.Data.Models;
using Octans.Data.Models.Duplicates;
using Octans.Data.Models.Maintenance;

namespace Octans.Client;

internal static class Endpoints
{
    public static void AddEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        MapFileEndpoints(api);

        MapMediaMetadataEndpoints(api);

        MapNoteEndpoints(api);

        MapTagEndpoints(api);

        MapRepositoryEndpoints(api);

        MapDuplicateEndpoints(api);

        MapDownloadEndpoints(api);

        MapDownloaderEndpoints(api);

        MapImportJobEndpoints(api);

        MapStorageMaintenanceEndpoints(api);

        MapInfrastructureEndpoints(api);
    }

    private static void MapTagEndpoints(IEndpointRouteBuilder app)
    {
        app
            .MapGet("/media/{hash}/tags",
                async (string hash, [FromServices] ITagService tagService) =>
                {
                    if (!TryNormalizeHash(hash, out var normalized, out var error))
                    {
                        return Results.BadRequest(error);
                    }

                    return Results.Ok(await tagService.GetTagsForHashAsync(normalized));
                })
            .WithName("GetMediaTags")
            .WithDescription("Gets tags for a media hash");

        app
            .MapGet("/tags/suggestions",
                async (string search,
                    bool? exact,
                    [FromServices] QuerySuggestionFinder suggestionFinder,
                    CancellationToken token) =>
                {
                    var suggestions = await suggestionFinder.GetAutocompleteTagIds(search, exact ?? false, token);
                    var tags = suggestions
                        .OrderBy(t => t.Namespace.Value)
                        .ThenBy(t => t.Subtag.Value)
                        .Select(t => new TagModel(t.Namespace.Value, t.Subtag.Value))
                        .ToList();

                    return new QuerySuggestionsDto(tags);
                })
            .WithName("GetTagSuggestions")
            .WithDescription("Gets autocomplete tag suggestions");

        app
            .MapPost("/tags",
                async ([FromBody] UpdateTagsRequest request, [FromServices] TagUpdater updater) =>
                {
                    var success = await updater.UpdateTags(request);

                    return success is TagUpdateResult.TagsUpdated ? Results.Ok() : Results.BadRequest();
                })
            .WithName("UpdateTags")
            .WithDescription("Add and remove tags for a specific image");
    }

    private static void MapDownloaderEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/downloaders/overview",
            async ([FromServices] IDownloaderFactory ds) =>
            {
                var downloaders = await ds.GetDownloaders();

                return new DownloadersOverviewDto(
                    ds.DownloaderDirectory,
                    downloaders.Select(d => d.Metadata).ToList());
            })
            .WithName("GetDownloadersOverview");

        app.MapGet("/downloaders",
            async ([FromServices] IDownloaderFactory ds) =>
            {
                var downloaders = await ds.GetDownloaders();

                return downloaders.Select(d => d.Metadata);
            });

        app.MapGet("/downloaders/{name}",
            async (string name, [FromServices] IDownloaderFactory ds) =>
            {
                var downloaders = await ds.GetDownloaders();

                var downloader = downloaders.SingleOrDefault(s => s.Metadata.Name == name);

                return downloader;
            });

        app.MapPost("/downloaders/rescan",
            async ([FromServices] IDownloaderFactory ds) =>
            {
                var downloaders = await ds.Rescan();

                return new DownloadersOverviewDto(
                    ds.DownloaderDirectory,
                    downloaders.Select(d => d.Metadata).ToList());
            })
            .WithName("RescanDownloaders");
    }

    private static void MapFileEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/files",
            async ([FromServices] FileFinder service) =>
            {
                var files = await service.GetAll();

                return files.Select(MapFile).ToList();
            });

        app
            .MapGet("/files/{id:int}",
                async (int id, [FromServices] FileFinder service) =>
                {
                    var file = await service.GetFile(id);

                    return file is null ? Results.NotFound() : Results.Ok(file);
                })
            .WithDescription("Get a single file by its ID");

        app
            .MapPost("/files/query",
                ([FromBody] IEnumerable<string> queries,
                    [FromServices] IQueryService service,
                    CancellationToken token) => QueryFileDtos(queries, service, token))
            .WithName("Search by Query")
            .WithDescription("Retrieve files found by a tag query search");

        app
            .MapPost("/files/query/count",
                async ([FromBody] IEnumerable<string> queries,
                    [FromServices] IQueryService service,
                    CancellationToken token) =>
                {
                    var count = 0;
                    await foreach (var _ in service.Query(queries, token))
                    {
                        count++;
                    }

                    return new FileQueryCountDto(count);
                })
            .WithName("CountSearchResults")
            .WithDescription("Counts files found by a tag query search");

        app
            .MapPost("/files",
                async ([FromBody] ImportRequest request,
                    [FromServices] IImportJobService service,
                    CancellationToken token) =>
                {
                    var created = await service.Create(ImportJobCreateRequestFromImportRequest(request), token);

                    return Results.Accepted($"/api/import-jobs/{created.JobId}", created);
                })
            .WithName("Import")
            .WithDescription("Creates an import job");

        app
            .MapPost("/files/deletion",
                async ([FromBody] DeleteRequest request, [FromServices] FileDeleter deleter) =>
                {
                    var results = await deleter.ProcessDeletion(request.Ids);

                    return new DeleteResponse(results);
                })
            .WithDescription("Delete one or more files and their associated data");
    }

    private static void MapMediaMetadataEndpoints(IEndpointRouteBuilder app)
    {
        app
            .MapGet("/media/{hash}/details",
                async (string hash,
                    [FromServices] ServerDbContext db,
                    [FromServices] ITagService tagService,
                    [FromServices] INoteService noteService) =>
                {
                    if (!TryNormalizeHash(hash, out var normalized, out var error))
                    {
                        return Results.BadRequest(error);
                    }

                    var bytes = Convert.FromHexString(normalized);
                    var item = await db.Hashes
                        .AsNoTracking()
                        .FirstOrDefaultAsync(h => h.Hash == bytes);

                    if (item is null)
                    {
                        return Results.NotFound();
                    }

                    var tags = await tagService.GetTagsForHashAsync(normalized);
                    var notes = await noteService.GetNotesAsync(normalized);
                    var dto = new MediaDetailsDto(
                        item.Id,
                        normalized,
                        item.Extension,
                        item.ContentType,
                        (RepositoryType)item.RepositoryId,
                        tags,
                        notes,
                        $"/media/{normalized}");

                    return Results.Ok(dto);
                })
            .WithName("GetMediaDetails")
            .WithDescription("Gets metadata, repository, tags, notes, and media URL for a media hash");
    }

    private static void MapNoteEndpoints(IEndpointRouteBuilder app)
    {
        app
            .MapGet("/media/{hash}/notes",
                async (string hash, [FromServices] INoteService noteService) =>
                {
                    if (!TryNormalizeHash(hash, out var normalized, out var error))
                    {
                        return Results.BadRequest(error);
                    }

                    return Results.Ok(await noteService.GetNotesAsync(normalized));
                })
            .WithName("GetMediaNotes");

        app
            .MapPost("/media/{hash}/notes",
                async (string hash, [FromBody] NoteCreateRequest request, [FromServices] INoteService noteService) =>
                {
                    if (!TryNormalizeHash(hash, out var normalized, out var error))
                    {
                        return Results.BadRequest(error);
                    }

                    if (string.IsNullOrWhiteSpace(request.Content))
                    {
                        return Results.BadRequest("Note content is required.");
                    }

                    try
                    {
                        var note = await noteService.AddNoteAsync(normalized, request.Content);

                        return Results.Created($"/api/notes/{note.Id}", note);
                    }
                    catch (ArgumentException ex)
                    {
                        return Results.NotFound(ex.Message);
                    }
                })
            .WithName("CreateMediaNote");

        app
            .MapPut("/notes/{id:int}",
                async (int id, [FromBody] NoteUpdateRequest request, [FromServices] INoteService noteService) =>
                {
                    if (string.IsNullOrWhiteSpace(request.Content))
                    {
                        return Results.BadRequest("Note content is required.");
                    }

                    try
                    {
                        await noteService.UpdateNoteAsync(id, request.Content);

                        return Results.NoContent();
                    }
                    catch (ArgumentException ex)
                    {
                        return Results.NotFound(ex.Message);
                    }
                })
            .WithName("UpdateMediaNote");

        app
            .MapDelete("/notes/{id:int}",
                async (int id, [FromServices] INoteService noteService) =>
                {
                    await noteService.DeleteNoteAsync(id);

                    return Results.NoContent();
                })
            .WithName("DeleteMediaNote");
    }

    private static void MapRepositoryEndpoints(IEndpointRouteBuilder app)
    {
        app
            .MapPost("/repository/transitions",
                async ([FromBody] RepositoryTransitionRequest request,
                    [FromServices] ChannelWriter<RepositoryChangeRequest> channel,
                    CancellationToken token) =>
                {
                    if (request.Hashes.Count == 0)
                    {
                        return Results.BadRequest("At least one hash is required.");
                    }

                    var normalizedHashes = new List<string>(request.Hashes.Count);
                    foreach (var hash in request.Hashes)
                    {
                        if (!TryNormalizeHash(hash, out var normalized, out var error))
                        {
                            return Results.BadRequest(error);
                        }

                        normalizedHashes.Add(normalized);
                    }

                    foreach (var hash in normalizedHashes)
                    {
                        await channel.WriteAsync(new(hash, request.Destination), token);
                    }

                    return Results.Accepted();
                })
            .WithName("TransitionRepositoryItems")
            .WithDescription("Moves one or more media hashes to inbox, archive, or trash");
    }

    private static void MapDuplicateEndpoints(IEndpointRouteBuilder app)
    {
        app
            .MapPost("/duplicates/scan",
                async ([FromServices] DuplicateService duplicateService, CancellationToken token) =>
                {
                    var calculated = await duplicateService.CalculateMissingHashes(token);
                    var created = await duplicateService.FindDuplicates(token);

                    return new DuplicateScanResultDto(calculated, created);
                })
            .WithName("ScanDuplicates");

        app
            .MapGet("/duplicates/candidates",
                async ([FromServices] ServerDbContext db, CancellationToken token) =>
                {
                    var candidates = await db.DuplicateCandidates
                        .Include(c => c.Hash1)
                        .Include(c => c.Hash2)
                        .AsNoTracking()
                        .OrderBy(c => c.Id)
                        .ToListAsync(token);

                    return candidates
                        .Select(c => new DuplicateCandidateDto(
                            c.Id,
                            c.HashId1,
                            Convert.ToHexString(c.Hash1.Hash),
                            $"/media/{Convert.ToHexString(c.Hash1.Hash)}",
                            c.HashId2,
                            Convert.ToHexString(c.Hash2.Hash),
                            $"/media/{Convert.ToHexString(c.Hash2.Hash)}",
                            c.Distance))
                        .ToList();
                })
            .WithName("GetDuplicateCandidates");

        app
            .MapPost("/duplicates/candidates/{id:int}/resolution",
                async (int id,
                    [FromBody] DuplicateResolutionRequest request,
                    [FromServices] DuplicateService duplicateService,
                    CancellationToken token) =>
                {
                    try
                    {
                        await duplicateService.Resolve(id, request.Resolution, request.KeepHashId, token);

                        return Results.NoContent();
                    }
                    catch (ArgumentException ex)
                    {
                        return Results.BadRequest(ex.Message);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.Problem(ex.Message);
                    }
                })
            .WithName("ResolveDuplicateCandidate");
    }

    private static void MapDownloadEndpoints(IEndpointRouteBuilder app)
    {
        app
            .MapPost("/downloads",
                async ([FromBody] DownloadQueueRequest request, [FromServices] IDownloadService downloads) =>
                {
                    if (string.IsNullOrWhiteSpace(request.DestinationPath))
                    {
                        return Results.BadRequest("Destination path is required.");
                    }

                    var downloadRequest = new DownloadRequest
                    {
                        Url = request.Url,
                        DestinationPath = request.DestinationPath,
                        DisplayName = request.DisplayName,
                        SourceType = request.SourceType,
                        SourceId = request.SourceId,
                        Priority = request.Priority
                    };

                    foreach (var contentType in request.AllowedContentTypes)
                    {
                        downloadRequest.AllowedContentTypes.Add(contentType);
                    }

                    foreach (var expectedHash in request.ExpectedHashes)
                    {
                        downloadRequest.ExpectedHashes.Add(expectedHash);
                    }

                    var handle = await downloads.QueueDownloadJobAsync(downloadRequest);

                    return Results.Accepted(
                        $"/api/downloads/{handle.Id}",
                        new DownloadQueuedDto(handle.Id, $"/api/downloads/{handle.Id}", $"/api/downloads/{handle.Id}/result"));
                })
            .WithName("QueueDownload");

        app
            .MapGet("/downloads",
                ([FromServices] IDownloadStateService stateService) =>
                    stateService.GetAllDownloads().Select(MapDownloadStatus).ToList())
            .WithName("GetDownloads");

        app
            .MapGet("/downloads/{id:guid}",
                async (Guid id,
                    [FromServices] IDownloadStateService stateService,
                    [FromServices] ServerDbContext db,
                    CancellationToken token) =>
                {
                    var active = stateService.GetDownloadById(id);
                    if (active is not null)
                    {
                        return Results.Ok(MapDownloadStatus(active));
                    }

                    var status = await db.DownloadStatuses
                        .AsNoTracking()
                        .FirstOrDefaultAsync(d => d.Id == id, token);

                    return status is null ? Results.NotFound() : Results.Ok(MapDownloadStatus(status));
                })
            .WithName("GetDownloadStatus");

        app
            .MapGet("/downloads/{id:guid}/result",
                async (Guid id, [FromServices] IDownloadService downloads, CancellationToken token) =>
                {
                    var result = await downloads.GetResultAsync(id, token);

                    return result is null ? Results.NotFound() : Results.Ok(result);
                })
            .WithName("GetDownloadResult");

        app
            .MapPost("/downloads/{id:guid}/pause",
                async (Guid id, [FromServices] IDownloadService downloads) =>
                {
                    await downloads.PauseDownloadAsync(id);

                    return Results.NoContent();
                })
            .WithName("PauseDownload");

        app
            .MapPost("/downloads/{id:guid}/resume",
                async (Guid id, [FromServices] IDownloadService downloads) =>
                {
                    await downloads.ResumeDownloadAsync(id);

                    return Results.NoContent();
                })
            .WithName("ResumeDownload");

        app
            .MapPost("/downloads/{id:guid}/cancel",
                async (Guid id, [FromServices] IDownloadService downloads) =>
                {
                    await downloads.CancelDownloadAsync(id);

                    return Results.NoContent();
                })
            .WithName("CancelDownload");

        app
            .MapPost("/downloads/{id:guid}/retry",
                async (Guid id, [FromServices] IDownloadService downloads) =>
                {
                    await downloads.RetryDownloadAsync(id);

                    return Results.NoContent();
                })
            .WithName("RetryDownload");
    }

    private static void MapImportJobEndpoints(IEndpointRouteBuilder app)
    {
        app
            .MapPost("/import-jobs",
                async ([FromBody] ImportJobCreateRequest request,
                    [FromServices] IImportJobService service,
                    CancellationToken token) =>
                {
                    var created = await service.Create(request, token);

                    return Results.Accepted($"/api/import-jobs/{created.JobId}", created);
                })
            .WithName("CreateImportJob")
            .WithDescription("Creates a durable import job");

        app
            .MapGet("/import-jobs",
                async ([FromServices] IImportJobService service, CancellationToken token) =>
                    await service.GetJobs(token))
            .WithName("GetImportJobs")
            .WithDescription("Gets recent import jobs");

        app
            .MapGet("/import-jobs/{id:guid}",
                async (Guid id, [FromServices] IImportJobService service, CancellationToken token) =>
                {
                    var job = await service.GetJob(id, token);

                    return job is null ? Results.NotFound() : Results.Ok(job);
                })
            .WithName("GetImportJob")
            .WithDescription("Gets a durable import job");

        app
            .MapPost("/import-jobs/{id:guid}/pause",
                async (Guid id, [FromServices] IImportJobService service, CancellationToken token) =>
                {
                    var job = await service.PauseJob(id, token);

                    return job is null ? Results.NotFound() : Results.Ok(job);
                })
            .WithName("PauseImportJob");

        app
            .MapPost("/import-jobs/{id:guid}/resume",
                async (Guid id, [FromServices] IImportJobService service, CancellationToken token) =>
                {
                    var job = await service.ResumeJob(id, token);

                    return job is null ? Results.NotFound() : Results.Ok(job);
                })
            .WithName("ResumeImportJob");

        app
            .MapPost("/import-jobs/{id:guid}/cancel",
                async (Guid id, [FromServices] IImportJobService service, CancellationToken token) =>
                {
                    var job = await service.CancelJob(id, token);

                    return job is null ? Results.NotFound() : Results.Ok(job);
                })
            .WithName("CancelImportJob");
    }

    private static void MapStorageMaintenanceEndpoints(IEndpointRouteBuilder app)
    {
        var maintenance = app.MapGroup("/maintenance/storage");

        maintenance
            .MapPost("/scans",
                async ([FromServices] IStorageMaintenanceService service, CancellationToken token) =>
                {
                    var created = await service.QueueScanAsync(StorageMaintenanceTrigger.Manual, token);
                    return Results.Accepted($"/api/maintenance/storage/jobs/{created.JobId}", created);
                })
            .WithName("QueueStorageMaintenanceScan")
            .WithDescription("Queues a durable content-store health scan");

        maintenance
            .MapPost("/scans/{scanJobId:guid}/repairs",
                async (Guid scanJobId,
                    [FromBody] StorageRepairRequest request,
                    [FromServices] IStorageMaintenanceService service,
                    CancellationToken token) =>
                {
                    try
                    {
                        var created = await service.QueueRepairAsync(scanJobId, request.Actions, token);
                        return Results.Accepted($"/api/maintenance/storage/jobs/{created.JobId}", created);
                    }
                    catch (ArgumentException ex)
                    {
                        return Results.BadRequest(ex.Message);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.Conflict(ex.Message);
                    }
                })
            .WithName("QueueStorageMaintenanceRepair")
            .WithDescription("Queues safe repairs for the open findings from a completed scan");

        maintenance
            .MapGet("/jobs",
                async ([FromServices] IStorageMaintenanceService service, CancellationToken token) =>
                    await service.GetJobsAsync(token))
            .WithName("GetStorageMaintenanceJobs");

        maintenance
            .MapGet("/jobs/{id:guid}",
                async (Guid id, [FromServices] IStorageMaintenanceService service, CancellationToken token) =>
                {
                    var job = await service.GetJobAsync(id, token);
                    return job is null ? Results.NotFound() : Results.Ok(job);
                })
            .WithName("GetStorageMaintenanceJob");

        maintenance
            .MapGet("/scans/{scanJobId:guid}/findings",
                async (Guid scanJobId,
                    StorageFindingResolution? resolution,
                    StorageFindingType? type,
                    int? skip,
                    int? take,
                    [FromServices] IStorageMaintenanceService service,
                    CancellationToken token) =>
                {
                    var page = await service.GetFindingsAsync(
                        scanJobId,
                        resolution,
                        type,
                        skip ?? 0,
                        take ?? 200,
                        token);
                    return page is null ? Results.NotFound() : Results.Ok(page);
                })
            .WithName("GetStorageMaintenanceFindings");

        maintenance
            .MapPost("/jobs/{id:guid}/cancel",
                async (Guid id, [FromServices] IStorageMaintenanceService service, CancellationToken token) =>
                {
                    var job = await service.CancelAsync(id, token);
                    return job is null ? Results.NotFound() : Results.Ok(job);
                })
            .WithName("CancelStorageMaintenanceJob");
    }

    private static ImportJobCreateRequest ImportJobCreateRequestFromImportRequest(ImportRequest request) => new()
    {
        ImportType = request.ImportType,
        Sources = request.Items.Select(item => item.Filepath ?? item.Url?.AbsoluteUri ?? string.Empty)
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .ToList(),
        DeleteAfterImport = request.DeleteAfterImport,
        AllowReimportDeleted = request.AllowReimportDeleted,
        AutoArchive = request.AutoArchive,
        FilterData = request.FilterData,
        TagsBySource = request.Items
            .Select(item => new
            {
                Source = item.Filepath ?? item.Url?.AbsoluteUri ?? string.Empty,
                item.Tags
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Source) && item.Tags is not null)
            .ToDictionary(item => item.Source, item => item.Tags!)
    };

    private static void MapInfrastructureEndpoints(IEndpointRouteBuilder app)
    {
        app
            .MapGet("/subscriptions",
                async ([FromServices] ISubscriptionService subscriptionService) =>
                    await subscriptionService.GetAllAsync())
            .WithName("GetSubscriptions")
            .WithDescription("Lists configured subscriptions");

        app
            .MapPost("/subscriptions",
                async ([FromBody] SubscriptionCreateRequest request,
                    [FromServices] ISubscriptionService subscriptionService) =>
                {
                    if (string.IsNullOrWhiteSpace(request.Name))
                    {
                        return Results.BadRequest("Subscription name is required.");
                    }

                    if (string.IsNullOrWhiteSpace(request.DownloaderName))
                    {
                        return Results.BadRequest("Downloader name is required.");
                    }

                    if (string.IsNullOrWhiteSpace(request.Query))
                    {
                        return Results.BadRequest("Subscription query is required.");
                    }

                    if (request.FrequencyMinutes <= 0)
                    {
                        return Results.BadRequest("Frequency must be greater than zero minutes.");
                    }

                    await subscriptionService.AddAsync(
                        request.Name,
                        request.DownloaderName,
                        request.Query,
                        TimeSpan.FromMinutes(request.FrequencyMinutes),
                        MapSubscriptionImportSettings(request.ImportOptions),
                        request.Tags);

                    return Results.Accepted("/api/subscriptions");
                })
            .WithName("SubmitSubscription")
            .WithDescription("Submits a subscription request for automated queries");

        app
            .MapDelete("/subscriptions/{id:int}",
                async (int id, [FromServices] ISubscriptionService subscriptionService) =>
                {
                    await subscriptionService.DeleteAsync(id);

                    return Results.NoContent();
                })
            .WithName("DeleteSubscription")
            .WithDescription("Deletes a subscription");

        app.MapPost("/clearAllData",
            async ([FromServices] ServerDbContext db) =>
            {
                db.Hashes.RemoveRange(db.Hashes);
                db.Mappings.RemoveRange(db.Mappings);
                db.Tags.RemoveRange(db.Tags);
                db.TagParents.RemoveRange(db.TagParents);
                db.TagSiblings.RemoveRange(db.TagSiblings);
                db.Namespaces.RemoveRange(db.Namespaces);
                db.Subtags.RemoveRange(db.Subtags);
                db.FileRecords.RemoveRange(db.FileRecords);

                await db.SaveChangesAsync();
            });

        app.MapHealthChecks("/health",
            new()
            {
                ResponseWriter = async (context, report) =>
                {
                    context.Response.ContentType = "text/plain";
                    await context.Response.WriteAsync(report.Status.ToString());
                }
            });

        app
            .MapGet("/stats", async ([FromServices] StatsService statsService) => await statsService.GetHomeStats())
            .WithName("GetHomeStats")
            .WithDescription("Returns statistics for the homepage");

        app
            .MapGet("/version",
                () => new
                {
                    Version = "1.0.0"
                })
            .WithName("GetVersion")
            .WithDescription("Returns the current API version");
    }

    private static SubscriptionImportSettings MapSubscriptionImportSettings(
        SubscriptionImportOptionsDto? importOptions) =>
        importOptions is null
            ? SubscriptionImportSettings.Default
            : new(
                importOptions.Repository,
                importOptions.AllowReimportDeleted,
                importOptions.AutoArchive);

    public static void MapImageEndpoints(this WebApplication app)
    {
        app.MapGet("/media/{hash}",
            async (HttpContext http,
                string hash,
                [FromServices] ImageStorage imageStorage,
                [FromServices] ServerDbContext db) =>
            {
                if (string.IsNullOrWhiteSpace(hash))
                {
                    return Results.BadRequest("Invalid hash.");
                }

                ContentHash contentHash;

                try
                {
                    contentHash = ContentHash.FromHex(hash);
                }
                catch
                {
                    return Results.BadRequest("Hash must be hex.");
                }

                var hashBytes = contentHash.Bytes;
                var hashItem = await db.Hashes.FirstOrDefaultAsync(h => h.Hash == hashBytes);
                var info = imageStorage.FindOriginal(contentHash, hashItem?.Extension);

                if (info is null || !info.Exists)
                {
                    return Results.NotFound();
                }

                var contentType = hashItem?.ContentType;
                if (string.IsNullOrWhiteSpace(contentType))
                {
                    var provider = new FileExtensionContentTypeProvider();

                    if (!provider.TryGetContentType(info.FullName, out contentType))
                    {
                        contentType = "application/octet-stream";
                    }
                }

                // ETag derived from the content hash you're already using.
                // (Quotes are required around the tag string.)
                var etag = new EntityTagHeaderValue($"\"{hash.ToUpperInvariant()}\"");

                http.Response.Headers[HeaderNames.CacheControl] = "public, max-age=31536000, immutable";

                return Results.Stream(info.OpenRead(),
                    contentType: contentType,
                    lastModified: info.LastWriteTimeUtc,
                    entityTag: etag,
                    enableRangeProcessing: true);
            });
    }

    private static bool TryNormalizeHash(string hash, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(hash))
        {
            error = "Hash is required.";
            return false;
        }

        try
        {
            normalized = ContentHash.FromHex(hash).Hex;
            return true;
        }
        catch
        {
            error = "Hash must be hex.";
            return false;
        }
    }

    private static async IAsyncEnumerable<FileDto> QueryFileDtos(
        IEnumerable<string> queries,
        IQueryService service,
        [EnumeratorCancellation] CancellationToken token)
    {
        await foreach (var item in service.Query(queries, token).WithCancellation(token))
        {
            yield return MapFile(item);
        }
    }

    private static FileDto MapFile(HashItem item)
    {
        var hash = ContentHash.FromHashBytes(item.Hash).Hex;

        return new(
            item.Id,
            hash,
            item.Extension,
            item.ContentType,
            (RepositoryType)item.RepositoryId,
            $"/media/{hash}");
    }

    private static DownloadStatusDto MapDownloadStatus(DownloadStatus status) => new(
        status.Id,
        status.Url,
        status.Filename,
        status.DisplayName,
        status.DestinationPath,
        status.Domain,
        status.Priority,
        status.TotalBytes,
        status.BytesDownloaded,
        status.ProgressPercentage,
        status.CurrentSpeed,
        status.State,
        status.TerminalOutcome,
        status.ErrorMessage,
        status.FailureCategory,
        status.HttpStatusCode,
        status.ResponseContentType,
        status.ResponseETag,
        status.ResponseLastModified,
        status.ValidationMessage,
        status.SourceType,
        status.SourceId,
        status.CreatedAt,
        status.StartedAt,
        status.CompletedAt,
        status.LastUpdated);
}
