using Octans.Core;
using Octans.Core.Downloaders;
using Octans.Core.Importing;
using Octans.Core.Models;
using Octans.Server.Services;
using Refit;

namespace Octans.Tests;

public interface IOctansApi
{
    [Post("/tags")]
    Task<IApiResponse> UpdateTags([Body] UpdateTagsRequest request);

    [Get("/downloaders")]
    Task<IApiResponse<IEnumerable<DownloaderMetadata>>> GetDownloaders();

    [Get("/downloaders/{name}")]
    Task<IApiResponse<Downloader>> GetDownloader(string name);

    [Get("/files")]
    Task<IApiResponse<IEnumerable<FileRecord>>> GetAllFiles();

    [Get("/files/{id}")]
    Task<IApiResponse<FileRecord>> GetFile(int id);

    [Post("/files/query")]
    Task<IApiResponse<IEnumerable<FileRecord>>> SearchByQuery([Body] IEnumerable<string> queries);

    [Post("/files")]
    Task<IApiResponse<ImportResult>> ProcessImport([Body] ImportRequest request);

    [Post("/files/deletion")]
    Task<IApiResponse<DeleteResponse>> DeleteFiles(DeleteRequest request);
}