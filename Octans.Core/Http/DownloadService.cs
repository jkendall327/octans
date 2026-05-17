using Octans.Core.Http.Models;

namespace Octans.Core.Http;

/// <summary>
/// Feature-facing entry point for submitting and controlling durable download jobs.
/// </summary>
public interface IDownloadService
{
    Task<Guid> QueueDownloadAsync(DownloadRequest request);
    Task<DownloadJobHandle> QueueDownloadJobAsync(DownloadRequest request);
    Task<DownloadJobResult> QueueDownloadAndWaitAsync(
        DownloadRequest request,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default);
    Task<DownloadJobResult?> GetResultAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DownloadJobResult?> GetResultAsync(DownloadJobHandle handle, CancellationToken cancellationToken = default);
    Task<DownloadJobResult> WaitForCompletionAsync(
        Guid id,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default);
    Task<DownloadJobResult> WaitForCompletionAsync(
        DownloadJobHandle handle,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default);
    Task CancelDownloadAsync(Guid id);
    Task PauseDownloadAsync(Guid id);
    Task ResumeDownloadAsync(Guid id);
    Task RetryDownloadAsync(Guid id);
}

/// <summary>
/// Thin facade that keeps callers on the stable download API while delegating
/// lifecycle changes and terminal-result queries to dedicated services.
/// </summary>
public sealed class DownloadService(
    IDownloadLifecycleService lifecycle,
    IDownloadJobResultService jobResults,
    IDownloadCompletionWaiter completionWaiter) : IDownloadService
{
    public Task<Guid> QueueDownloadAsync(DownloadRequest request) => lifecycle.QueueDownloadAsync(request);
    public async Task<DownloadJobHandle> QueueDownloadJobAsync(DownloadRequest request) =>
        new(await lifecycle.QueueDownloadAsync(request));
    public async Task<DownloadJobResult> QueueDownloadAndWaitAsync(
        DownloadRequest request,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        var handle = await QueueDownloadJobAsync(request);
        return await completionWaiter.WaitForCompletionAsync(handle, pollInterval, cancellationToken);
    }

    public Task<DownloadJobResult?> GetResultAsync(Guid id, CancellationToken cancellationToken = default) =>
        jobResults.GetResultAsync(id, cancellationToken);
    public Task<DownloadJobResult?> GetResultAsync(
        DownloadJobHandle handle,
        CancellationToken cancellationToken = default) =>
        jobResults.GetResultAsync(handle, cancellationToken);
    public Task<DownloadJobResult> WaitForCompletionAsync(
        Guid id,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default) =>
        completionWaiter.WaitForCompletionAsync(id, pollInterval, cancellationToken);
    public Task<DownloadJobResult> WaitForCompletionAsync(
        DownloadJobHandle handle,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default) =>
        completionWaiter.WaitForCompletionAsync(handle, pollInterval, cancellationToken);
    public Task CancelDownloadAsync(Guid id) => lifecycle.CancelDownloadAsync(id);
    public Task PauseDownloadAsync(Guid id) => lifecycle.PauseDownloadAsync(id);
    public Task ResumeDownloadAsync(Guid id) => lifecycle.ResumeDownloadAsync(id);
    public Task RetryDownloadAsync(Guid id) => lifecycle.RetryDownloadAsync(id);
}
