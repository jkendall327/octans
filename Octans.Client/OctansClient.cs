using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Octans.Core;
using Octans.Core.Downloaders;
using Octans.Core.Filesystem;
using Octans.Core.Http.Models;
using Octans.Core.Importing;
using Octans.Core.Maintenance;
using Octans.Core.Notes;
using Octans.Core.Repositories;
using Octans.Core.Stats;
using Octans.Core.Subscriptions;
using Octans.Core.Tags;
using Octans.Data.Models.Duplicates;
using Octans.Data.Models.Maintenance;

namespace Octans.Client;

public interface IOctansClient
{
    Task<IReadOnlyList<FileDto>> GetFilesAsync(CancellationToken cancellationToken = default);
    Task<string?> GetFileAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FileDto>> QueryFilesAsync(IEnumerable<string> queries, CancellationToken cancellationToken = default);
    Task<int> CountQueryFilesAsync(IEnumerable<string> queries, CancellationToken cancellationToken = default);
    Task<MediaDetailsDto?> GetMediaDetailsAsync(string hash, CancellationToken cancellationToken = default);
    Task<NoteDto> AddNoteAsync(string hash, string content, CancellationToken cancellationToken = default);
    Task UpdateNoteAsync(int id, string content, CancellationToken cancellationToken = default);
    Task DeleteNoteAsync(int id, CancellationToken cancellationToken = default);
    Task TransitionRepositoryItemsAsync(
        IEnumerable<string> hashes,
        RepositoryDestination destination,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TagModel>> GetQuerySuggestionsAsync(
        string search,
        bool exact = false,
        CancellationToken cancellationToken = default);
    Task<ImportJobClientResult> ImportFilesAsync(ImportRequest request, CancellationToken cancellationToken = default);
    Task<ImportJobClientResult> CreateImportJobAsync(
        ImportJobCreateRequest request,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ImportJobDto>> GetImportJobsAsync(CancellationToken cancellationToken = default);
    Task<ImportJobDto?> GetImportJobAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ImportJobDto?> PauseImportJobAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ImportJobDto?> ResumeImportJobAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ImportJobDto?> CancelImportJobAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> UpdateTagsAsync(UpdateTagsRequest request, CancellationToken cancellationToken = default);
    Task<DeleteResponse> DeleteFilesAsync(DeleteRequest request, CancellationToken cancellationToken = default);
    Task<DeleteResponse> DeleteFilesAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DownloaderMetadata>> GetDownloadersAsync(CancellationToken cancellationToken = default);
    Task<DownloadersOverviewDto> GetDownloadersOverviewAsync(CancellationToken cancellationToken = default);
    Task<DownloadersOverviewDto> RescanDownloadersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubscriptionStatusDto>> GetSubscriptionsAsync(CancellationToken cancellationToken = default);
    Task AddSubscriptionAsync(SubscriptionCreateRequest request, CancellationToken cancellationToken = default);
    Task DeleteSubscriptionAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DownloadStatusDto>> GetDownloadsAsync(CancellationToken cancellationToken = default);
    Task<StorageMaintenanceJobCreated> QueueStorageScanAsync(CancellationToken cancellationToken = default);
    Task<StorageMaintenanceJobCreated> QueueStorageRepairAsync(
        Guid scanJobId,
        StorageRepairActions actions,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StorageMaintenanceJobDto>> GetStorageMaintenanceJobsAsync(
        CancellationToken cancellationToken = default);
    Task<StorageMaintenanceJobDto?> GetStorageMaintenanceJobAsync(
        Guid id,
        CancellationToken cancellationToken = default);
    Task<StorageMaintenanceFindingsPage?> GetStorageMaintenanceFindingsAsync(
        Guid scanJobId,
        StorageFindingResolution? resolution = null,
        StorageFindingType? type = null,
        int skip = 0,
        int take = 200,
        CancellationToken cancellationToken = default);
    Task<StorageMaintenanceJobDto?> CancelStorageMaintenanceJobAsync(
        Guid id,
        CancellationToken cancellationToken = default);
    Task<HomeStats> GetHomeStatsAsync(CancellationToken cancellationToken = default);
    Task<OctansVersion> GetVersionAsync(CancellationToken cancellationToken = default);
    Task<DuplicateScanResultDto> ScanDuplicatesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DuplicateCandidateDto>> GetDuplicateCandidatesAsync(
        CancellationToken cancellationToken = default);
    Task ResolveDuplicateCandidateAsync(
        int candidateId,
        DuplicateResolution resolution,
        int? keepHashId,
        CancellationToken cancellationToken = default);
    Task ClearAllDataAsync(CancellationToken cancellationToken = default);
    string GetMediaUrl(ContentHash hash);
    string GetMediaUrl(string hexHash);
}

public sealed class OctansClient(HttpClient httpClient) : IOctansClient
{
    private static readonly Uri ClearAllDataUri = Api("clearAllData");
    private static readonly Uri DuplicateScanUri = Api("duplicates/scan");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    public async Task<IReadOnlyList<FileDto>> GetFilesAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetFromJsonAsync<List<FileDto>>(Api("files"), JsonOptions, cancellationToken);

        return response ?? [];
    }

    public async Task<string?> GetFileAsync(int id, CancellationToken cancellationToken = default)
    {
        var uri = Api($"files/{id.ToString(CultureInfo.InvariantCulture)}");

        using var response = await httpClient.GetAsync(uri, cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<string>(JsonOptions, cancellationToken);
    }

    public async Task<IReadOnlyList<FileDto>> QueryFilesAsync(
        IEnumerable<string> queries,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(Api("files/query"), queries, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var files = await response.Content.ReadFromJsonAsync<List<FileDto>>(JsonOptions, cancellationToken);

        return files ?? [];
    }

    public async Task<int> CountQueryFilesAsync(
        IEnumerable<string> queries,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(Api("files/query/count"), queries, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var count = await ReadRequiredJsonAsync<FileQueryCountDto>(response, cancellationToken);

        return count.Count;
    }

    public async Task<MediaDetailsDto?> GetMediaDetailsAsync(
        string hash,
        CancellationToken cancellationToken = default)
    {
        var uri = Api($"media/{hash}/details");
        using var response = await httpClient.GetAsync(uri, cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);

        return await ReadRequiredJsonAsync<MediaDetailsDto>(response, cancellationToken);
    }

    public async Task<NoteDto> AddNoteAsync(
        string hash,
        string content,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            Api($"media/{hash}/notes"),
            new NoteCreateRequest(content),
            JsonOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await ReadRequiredJsonAsync<NoteDto>(response, cancellationToken);
    }

    public async Task UpdateNoteAsync(
        int id,
        string content,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PutAsJsonAsync(
            Api($"notes/{id.ToString(CultureInfo.InvariantCulture)}"),
            new NoteUpdateRequest(content),
            JsonOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DeleteNoteAsync(int id, CancellationToken cancellationToken = default)
    {
        var uri = Api($"notes/{id.ToString(CultureInfo.InvariantCulture)}");
        using var response = await httpClient.DeleteAsync(uri, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task TransitionRepositoryItemsAsync(
        IEnumerable<string> hashes,
        RepositoryDestination destination,
        CancellationToken cancellationToken = default)
    {
        var request = new RepositoryTransitionRequest(hashes.ToList(), destination);
        using var response = await httpClient.PostAsJsonAsync(
            Api("repository/transitions"),
            request,
            JsonOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<TagModel>> GetQuerySuggestionsAsync(
        string search,
        bool exact = false,
        CancellationToken cancellationToken = default)
    {
        var uri = Api($"tags/suggestions?search={Uri.EscapeDataString(search)}&exact={exact.ToString().ToLowerInvariant()}");
        var response = await httpClient.GetFromJsonAsync<QuerySuggestionsDto>(uri, JsonOptions, cancellationToken);

        return response?.Tags ?? [];
    }

    public async Task<ImportJobClientResult> ImportFilesAsync(
        ImportRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(Api("files"), request, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var created = await ReadRequiredJsonAsync<ImportJobCreatedDto>(response, cancellationToken);

        return new(created.JobId, $"/api/import-jobs/{created.JobId}");
    }

    public async Task<ImportJobClientResult> CreateImportJobAsync(
        ImportJobCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(Api("import-jobs"), request, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var created = await ReadRequiredJsonAsync<ImportJobCreatedDto>(response, cancellationToken);

        return new(created.JobId, $"/api/import-jobs/{created.JobId}");
    }

    public async Task<IReadOnlyList<ImportJobDto>> GetImportJobsAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetFromJsonAsync<List<ImportJobDto>>(
            Api("import-jobs"),
            JsonOptions,
            cancellationToken);

        return response ?? [];
    }

    public async Task<ImportJobDto?> GetImportJobAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var uri = Api($"import-jobs/{id}");
        using var response = await httpClient.GetAsync(uri, cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);

        return await ReadRequiredJsonAsync<ImportJobDto>(response, cancellationToken);
    }

    public Task<ImportJobDto?> PauseImportJobAsync(Guid id, CancellationToken cancellationToken = default) =>
        TransitionImportJobAsync(id, "pause", cancellationToken);

    public Task<ImportJobDto?> ResumeImportJobAsync(Guid id, CancellationToken cancellationToken = default) =>
        TransitionImportJobAsync(id, "resume", cancellationToken);

    public Task<ImportJobDto?> CancelImportJobAsync(Guid id, CancellationToken cancellationToken = default) =>
        TransitionImportJobAsync(id, "cancel", cancellationToken);

    public async Task<bool> UpdateTagsAsync(UpdateTagsRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(Api("tags"), request, JsonOptions, cancellationToken);

        if (response.StatusCode is HttpStatusCode.BadRequest)
        {
            return false;
        }

        await EnsureSuccessAsync(response, cancellationToken);

        return true;
    }

    public Task<DeleteResponse> DeleteFilesAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default)
    {
        return DeleteFilesAsync(new DeleteRequest(ids), cancellationToken);
    }

    public async Task<DeleteResponse> DeleteFilesAsync(
        DeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(Api("files/deletion"), request, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await ReadRequiredJsonAsync<DeleteResponse>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<DownloaderMetadata>> GetDownloadersAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetFromJsonAsync<List<DownloaderMetadata>>(
            Api("downloaders"),
            JsonOptions,
            cancellationToken);

        return response ?? [];
    }

    public async Task<DownloadersOverviewDto> GetDownloadersOverviewAsync(CancellationToken cancellationToken = default)
    {
        return await ReadRequiredJsonAsync<DownloadersOverviewDto>(Api("downloaders/overview"), cancellationToken);
    }

    public async Task<DownloadersOverviewDto> RescanDownloadersAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsync(Api("downloaders/rescan"), null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await ReadRequiredJsonAsync<DownloadersOverviewDto>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<SubscriptionStatusDto>> GetSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetFromJsonAsync<List<SubscriptionStatusDto>>(
            Api("subscriptions"),
            JsonOptions,
            cancellationToken);

        return response ?? [];
    }

    public async Task AddSubscriptionAsync(
        SubscriptionCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(Api("subscriptions"), request, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DeleteSubscriptionAsync(int id, CancellationToken cancellationToken = default)
    {
        var uri = Api($"subscriptions/{id.ToString(CultureInfo.InvariantCulture)}");
        using var response = await httpClient.DeleteAsync(uri, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<DownloadStatusDto>> GetDownloadsAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetFromJsonAsync<List<DownloadStatusDto>>(
            Api("downloads"),
            JsonOptions,
            cancellationToken);

        return response ?? [];
    }

    public async Task<StorageMaintenanceJobCreated> QueueStorageScanAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsync(Api("maintenance/storage/scans"), null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadRequiredJsonAsync<StorageMaintenanceJobCreated>(response, cancellationToken);
    }

    public async Task<StorageMaintenanceJobCreated> QueueStorageRepairAsync(
        Guid scanJobId,
        StorageRepairActions actions,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            Api($"maintenance/storage/scans/{scanJobId}/repairs"),
            new StorageRepairRequest(actions),
            JsonOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadRequiredJsonAsync<StorageMaintenanceJobCreated>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<StorageMaintenanceJobDto>> GetStorageMaintenanceJobsAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetFromJsonAsync<List<StorageMaintenanceJobDto>>(
            Api("maintenance/storage/jobs"),
            JsonOptions,
            cancellationToken);
        return response ?? [];
    }

    public async Task<StorageMaintenanceJobDto?> GetStorageMaintenanceJobAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(Api($"maintenance/storage/jobs/{id}"), cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadRequiredJsonAsync<StorageMaintenanceJobDto>(response, cancellationToken);
    }

    public async Task<StorageMaintenanceFindingsPage?> GetStorageMaintenanceFindingsAsync(
        Guid scanJobId,
        StorageFindingResolution? resolution = null,
        StorageFindingType? type = null,
        int skip = 0,
        int take = 200,
        CancellationToken cancellationToken = default)
    {
        var parameters = new List<string>
        {
            $"skip={skip.ToString(CultureInfo.InvariantCulture)}",
            $"take={take.ToString(CultureInfo.InvariantCulture)}"
        };
        if (resolution is not null)
        {
            parameters.Add($"resolution={resolution}");
        }

        if (type is not null)
        {
            parameters.Add($"type={type}");
        }

        var uri = Api($"maintenance/storage/scans/{scanJobId}/findings?{string.Join('&', parameters)}");
        using var response = await httpClient.GetAsync(uri, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadRequiredJsonAsync<StorageMaintenanceFindingsPage>(response, cancellationToken);
    }

    public async Task<StorageMaintenanceJobDto?> CancelStorageMaintenanceJobAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsync(
            Api($"maintenance/storage/jobs/{id}/cancel"),
            null,
            cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadRequiredJsonAsync<StorageMaintenanceJobDto>(response, cancellationToken);
    }

    public async Task<HomeStats> GetHomeStatsAsync(CancellationToken cancellationToken = default)
    {
        return await ReadRequiredJsonAsync<HomeStats>(Api("stats"), cancellationToken);
    }

    public async Task<OctansVersion> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        return await ReadRequiredJsonAsync<OctansVersion>(Api("version"), cancellationToken);
    }

    public async Task<DuplicateScanResultDto> ScanDuplicatesAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsync(DuplicateScanUri, null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await ReadRequiredJsonAsync<DuplicateScanResultDto>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<DuplicateCandidateDto>> GetDuplicateCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetFromJsonAsync<List<DuplicateCandidateDto>>(
            Api("duplicates/candidates"),
            JsonOptions,
            cancellationToken);

        return response ?? [];
    }

    public async Task ResolveDuplicateCandidateAsync(
        int candidateId,
        DuplicateResolution resolution,
        int? keepHashId,
        CancellationToken cancellationToken = default)
    {
        var uri = Api($"duplicates/candidates/{candidateId.ToString(CultureInfo.InvariantCulture)}/resolution");
        var request = new DuplicateResolutionRequest(resolution, keepHashId);

        using var response = await httpClient.PostAsJsonAsync(uri, request, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task ClearAllDataAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsync(ClearAllDataUri, null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public string GetMediaUrl(ContentHash hash)
    {
        return GetMediaUrl(hash.Hex);
    }

    public string GetMediaUrl(string hexHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hexHash);

        return $"/media/{hexHash.ToUpperInvariant()}";
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        throw new HttpRequestException(
            $"Octans API request failed with status {(int)response.StatusCode}: {content}",
            null,
            response.StatusCode);
    }

    private async Task<T> ReadRequiredJsonAsync<T>(Uri requestUri, CancellationToken cancellationToken)
    {
        var response = await httpClient.GetFromJsonAsync<T>(requestUri, JsonOptions, cancellationToken);

        return response ?? throw new InvalidOperationException($"Octans API returned an empty {typeof(T).Name} response.");
    }

    private static async Task<T> ReadRequiredJsonAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);

        return content ?? throw new InvalidOperationException($"Octans API returned an empty {typeof(T).Name} response.");
    }

    private async Task<ImportJobDto?> TransitionImportJobAsync(
        Guid id,
        string transition,
        CancellationToken cancellationToken)
    {
        var uri = Api($"import-jobs/{id}/{transition}");
        using var response = await httpClient.PostAsync(uri, null, cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);

        return await ReadRequiredJsonAsync<ImportJobDto>(response, cancellationToken);
    }

    private static Uri Api(string path) => new($"api/{path}", UriKind.Relative);
}

public sealed record OctansVersion(string Version);
