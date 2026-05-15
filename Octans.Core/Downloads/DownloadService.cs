using Octans.Core.Downloads.Models;

namespace Octans.Core.Downloads;

public interface IDownloadService
{
    Task<Guid> QueueDownloadAsync(DownloadRequest request);
    Task<DownloadJobHandle> QueueDownloadJobAsync(DownloadRequest request);
    Task<DownloadJobResult?> GetResultAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DownloadJobResult?> GetResultAsync(DownloadJobHandle handle, CancellationToken cancellationToken = default);
    Task CancelDownloadAsync(Guid id);
    Task PauseDownloadAsync(Guid id);
    Task ResumeDownloadAsync(Guid id);
    Task RetryDownloadAsync(Guid id);
}

public sealed class DownloadService(
    IDownloadLifecycleService lifecycle,
    IDownloadJobResultService jobResults) : IDownloadService
{
    public Task<Guid> QueueDownloadAsync(DownloadRequest request) => lifecycle.QueueDownloadAsync(request);
    public async Task<DownloadJobHandle> QueueDownloadJobAsync(DownloadRequest request) =>
        new(await lifecycle.QueueDownloadAsync(request));
    public Task<DownloadJobResult?> GetResultAsync(Guid id, CancellationToken cancellationToken = default) =>
        jobResults.GetResultAsync(id, cancellationToken);
    public Task<DownloadJobResult?> GetResultAsync(
        DownloadJobHandle handle,
        CancellationToken cancellationToken = default) =>
        jobResults.GetResultAsync(handle, cancellationToken);
    public Task CancelDownloadAsync(Guid id) => lifecycle.CancelDownloadAsync(id);
    public Task PauseDownloadAsync(Guid id) => lifecycle.PauseDownloadAsync(id);
    public Task ResumeDownloadAsync(Guid id) => lifecycle.ResumeDownloadAsync(id);
    public Task RetryDownloadAsync(Guid id) => lifecycle.RetryDownloadAsync(id);
}
