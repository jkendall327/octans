using Octans.Core.Importing;
using Octans.Core.Models;
using Octans.Server.Services;
using Refit;

namespace Octans.Core.Communication;

public interface IOctansApi
{
    [Post("/tags")]
    Task<IApiResponse> UpdateTags([Body] UpdateTagsRequest request);

    [Get("/files")]
    Task<IApiResponse<List<HashItem>>> GetAllFiles();

    [Post("/files")]
    Task<IApiResponse<ImportResult>> ProcessImport([Body] ImportRequest request);

    [Post("/files/deletion")]
    Task<IApiResponse<DeleteResponse>> DeleteFiles(DeleteRequest request);

    [Post("/subscriptions")]
    Task<IApiResponse> SubmitSubscription([Body] SubscriptionRequest request);

    [Get("/health")]
    Task<IApiResponse> HealthCheck();
}
