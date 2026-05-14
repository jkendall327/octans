using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Octans.Core;
using Octans.Core.Deletion;
using Octans.Core.Downloads.Downloaders;
using Octans.Core.Importing;
using Octans.Core.Stats;
using Octans.Core.Tags;
using Octans.Data.Models;

namespace Octans.Client;

public interface IOctansClient
{
    Task<IReadOnlyList<HashItem>> GetFilesAsync(CancellationToken cancellationToken = default);
    Task<string?> GetFileAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HashItem>> QueryFilesAsync(IEnumerable<string> queries, CancellationToken cancellationToken = default);
    Task<ImportResult> ImportFilesAsync(ImportRequest request, CancellationToken cancellationToken = default);
    Task<bool> UpdateTagsAsync(UpdateTagsRequest request, CancellationToken cancellationToken = default);
    Task<DeleteResponse> DeleteFilesAsync(DeleteRequest request, CancellationToken cancellationToken = default);
    Task<DeleteResponse> DeleteFilesAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DownloaderMetadata>> GetDownloadersAsync(CancellationToken cancellationToken = default);
    Task<HomeStats> GetHomeStatsAsync(CancellationToken cancellationToken = default);
    Task<OctansVersion> GetVersionAsync(CancellationToken cancellationToken = default);
    Task ClearAllDataAsync(CancellationToken cancellationToken = default);
    string GetMediaUrl(ContentHash hash);
    string GetMediaUrl(string hexHash);
}

public sealed class OctansClient(HttpClient httpClient) : IOctansClient
{
    private static readonly Uri ClearAllDataUri = new("clearAllData", UriKind.Relative);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    public async Task<IReadOnlyList<HashItem>> GetFilesAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetFromJsonAsync<List<HashItem>>("files", JsonOptions, cancellationToken);

        return response ?? [];
    }

    public async Task<string?> GetFileAsync(int id, CancellationToken cancellationToken = default)
    {
        var uri = new Uri($"files/{id.ToString(CultureInfo.InvariantCulture)}", UriKind.Relative);

        using var response = await httpClient.GetAsync(uri, cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<string>(JsonOptions, cancellationToken);
    }

    public async Task<IReadOnlyList<HashItem>> QueryFilesAsync(
        IEnumerable<string> queries,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("files/query", queries, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var files = await response.Content.ReadFromJsonAsync<List<HashItem>>(JsonOptions, cancellationToken);

        return files ?? [];
    }

    public async Task<ImportResult> ImportFilesAsync(
        ImportRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("files", request, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await ReadRequiredJsonAsync<ImportResult>(response, cancellationToken);
    }

    public async Task<bool> UpdateTagsAsync(UpdateTagsRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("tags", request, JsonOptions, cancellationToken);

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
        using var response = await httpClient.PostAsJsonAsync("files/deletion", request, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await ReadRequiredJsonAsync<DeleteResponse>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<DownloaderMetadata>> GetDownloadersAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetFromJsonAsync<List<DownloaderMetadata>>(
            "downloaders",
            JsonOptions,
            cancellationToken);

        return response ?? [];
    }

    public async Task<HomeStats> GetHomeStatsAsync(CancellationToken cancellationToken = default)
    {
        return await ReadRequiredJsonAsync<HomeStats>("stats", cancellationToken);
    }

    public async Task<OctansVersion> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        return await ReadRequiredJsonAsync<OctansVersion>("version", cancellationToken);
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

    private async Task<T> ReadRequiredJsonAsync<T>(string requestUri, CancellationToken cancellationToken)
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
}

public sealed record OctansVersion(string Version);
