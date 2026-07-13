using Octans.Core.Downloaders;
using Octans.Core.Importing;
using Octans.Core.Http.Models;
using Octans.Core.Notes;
using Octans.Core.Repositories;
using Octans.Core.Subscriptions;
using Octans.Core.Tags;
using Octans.Data.Models;
using Octans.Data.Models.Duplicates;

namespace Octans.Client;

public sealed record MediaDetailsDto(
    int Id,
    string Hash,
    string? Extension,
    string? ContentType,
    RepositoryType Repository,
    IReadOnlyList<TagModel> Tags,
    IReadOnlyList<NoteDto> Notes,
    string MediaUrl);

public sealed record FileDto(
    int Id,
    string Hash,
    string? Extension,
    string? ContentType,
    RepositoryType Repository,
    string MediaUrl);

public sealed record NoteCreateRequest(string Content);

public sealed record NoteUpdateRequest(string Content);

public sealed record RepositoryTransitionRequest(
    IReadOnlyList<string> Hashes,
    RepositoryDestination Destination);

public sealed record QuerySuggestionsDto(IReadOnlyList<TagModel> Tags);

public sealed record FileQueryCountDto(int Count);

public sealed record DownloadersOverviewDto(
    string DownloaderDirectory,
    IReadOnlyList<DownloaderMetadata> Downloaders);

public sealed record SubscriptionCreateRequest(
    string Name,
    string DownloaderName,
    string Query,
    int FrequencyMinutes,
    SubscriptionImportOptionsDto? ImportOptions = null,
    IReadOnlyList<TagModel>? Tags = null,
    int MaxItemsPerRun = 100);

public sealed record SubscriptionImportOptionsDto(
    RepositoryType Repository,
    bool AllowReimportDeleted,
    bool AutoArchive);

public sealed record DuplicateScanResultDto(
    int PerceptualHashesCalculated,
    int CandidatesCreated);

public sealed record DuplicateCandidateDto(
    int Id,
    int HashId1,
    string Hash1,
    string MediaUrl1,
    int HashId2,
    string Hash2,
    string MediaUrl2,
    double Distance);

public sealed record DuplicateResolutionRequest(
    DuplicateResolution Resolution,
    int? KeepHashId);

public sealed record DownloadQueueRequest
{
    public required Uri Url { get; init; }
    public required string DestinationPath { get; init; }
    public IReadOnlyList<string> AllowedContentTypes { get; init; } = [];
    public IReadOnlyList<DownloadHashExpectation> ExpectedHashes { get; init; } = [];
    public string? DisplayName { get; init; }
    public string? SourceType { get; init; }
    public string? SourceId { get; init; }
    public int Priority { get; init; }
}

public sealed record DownloadQueuedDto(
    Guid Id,
    string StatusUrl,
    string ResultUrl);

public sealed record DownloadStatusDto(
    Guid Id,
    string Url,
    string Filename,
    string? DisplayName,
    string DestinationPath,
    string Domain,
    int Priority,
    long TotalBytes,
    long BytesDownloaded,
    double ProgressPercentage,
    double CurrentSpeed,
    DownloadState State,
    DownloadTerminalOutcome? TerminalOutcome,
    string? ErrorMessage,
    DownloadFailureCategory? FailureCategory,
    int? HttpStatusCode,
    string? ResponseContentType,
    string? ResponseETag,
    DateTimeOffset? ResponseLastModified,
    string? ValidationMessage,
    string? SourceType,
    string? SourceId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset LastUpdated);

public sealed record ImportJobClientResult(
    Guid JobId,
    string StatusUrl);
