using Octans.Core.Downloads.Models;

namespace Octans.Core.Downloads;

public interface IDownloadService
{
    Task<Guid> QueueDownloadAsync(DownloadRequest request);
    Task CancelDownloadAsync(Guid id);
    Task PauseDownloadAsync(Guid id);
    Task ResumeDownloadAsync(Guid id);
    Task RetryDownloadAsync(Guid id);
}

public sealed class DownloadService(IDownloadLifecycleService lifecycle) : IDownloadService
{
    public Task<Guid> QueueDownloadAsync(DownloadRequest request) => lifecycle.QueueDownloadAsync(request);
    public Task CancelDownloadAsync(Guid id) => lifecycle.CancelDownloadAsync(id);
    public Task PauseDownloadAsync(Guid id) => lifecycle.PauseDownloadAsync(id);
    public Task ResumeDownloadAsync(Guid id) => lifecycle.ResumeDownloadAsync(id);
    public Task RetryDownloadAsync(Guid id) => lifecycle.RetryDownloadAsync(id);
}
