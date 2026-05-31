using Microsoft.Extensions.Options;
using Octans.Core.Http.Models;

namespace Octans.Core.Http;

/// <summary>
/// Waits for durable download jobs to reach a terminal result.
/// </summary>
public interface IDownloadCompletionWaiter
{
    Task<DownloadJobResult> WaitForCompletionAsync(
        Guid id,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default);

    Task<DownloadJobResult> WaitForCompletionAsync(
        DownloadJobHandle handle,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Async completion waiter backed by in-process state-change notifications and
/// a durable database polling fallback.
/// </summary>
internal sealed class DownloadCompletionWaiter(
    IDownloadStateService stateService,
    IDownloadJobResultService jobResults,
    TimeProvider timeProvider,
    IOptions<DownloadManagerOptions> options) : IDownloadCompletionWaiter
{
    public async Task<DownloadJobResult> WaitForCompletionAsync(
        Guid id,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        var existing = await jobResults.GetResultAsync(id, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var signal = new TaskCompletionSource<DownloadJobResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        stateService.DownloadStatusChanged += OnDownloadStatusChanged;

        try
        {
            existing = await jobResults.GetResultAsync(id, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            var effectivePollInterval = GetPollInterval(pollInterval);
            while (true)
            {
                var delay = Task.Delay(effectivePollInterval, timeProvider, cancellationToken);
                var completed = await Task.WhenAny(signal.Task, delay);

                if (completed == signal.Task)
                {
                    return await signal.Task;
                }

                await delay;

                existing = await jobResults.GetResultAsync(id, cancellationToken);
                if (existing is not null)
                {
                    return existing;
                }
            }
        }
        finally
        {
            stateService.DownloadStatusChanged -= OnDownloadStatusChanged;
        }

        ValueTask OnDownloadStatusChanged(DownloadStatusChanged notification)
        {
            if (notification.Status.Id == id &&
                DownloadJobResults.FromStatus(notification.Status) is { } result)
            {
                signal.TrySetResult(result);
            }

            return ValueTask.CompletedTask;
        }
    }

    public Task<DownloadJobResult> WaitForCompletionAsync(
        DownloadJobHandle handle,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default) =>
        WaitForCompletionAsync(handle.Id, pollInterval, cancellationToken);

    private TimeSpan GetPollInterval(TimeSpan? pollInterval)
    {
        var effectivePollInterval = pollInterval ?? options.Value.CompletionPollingInterval;
        if (effectivePollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pollInterval),
                "The download completion poll interval must be positive.");
        }

        return effectivePollInterval;
    }
}
