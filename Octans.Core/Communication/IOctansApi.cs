using Octans.Core.Downloaders;
using Octans.Core.Importing;
using Octans.Core.Models;
using Octans.Server.Services;
using Refit;

namespace Octans.Core.Communication;

public interface IOctansApi
{
    [Post("/tags")]
    Task<IApiResponse> UpdateTags([Body] UpdateTagsRequest request);

    [Get("/downloaders")]
    Task<IApiResponse<IEnumerable<DownloaderMetadata>>> GetDownloaders();

    [Get("/downloaders/{name}")]
    Task<IApiResponse<Downloader>> GetDownloader(string name);

    [Get("/files")]
    Task<IApiResponse<List<HashItem>>> GetAllFiles();

    [Get("/files/{id}")]
    Task<IApiResponse<string?>> GetFile(int id);

    [Post("/files/query")]
    Task<IApiResponse<HashSet<HashItem>>> SearchByQuery([Body] IEnumerable<string> queries);

    [Post("/files")]
    Task<IApiResponse<ImportResult>> ProcessImport([Body] ImportRequest request);

    [Post("/files/deletion")]
    Task<IApiResponse<DeleteResponse>> DeleteFiles(DeleteRequest request);

    [Get("/health")]
    Task<IApiResponse> HealthCheck();

    [Post("/clearAllData")]
    Task<IApiResponse> ClearAllData();
}