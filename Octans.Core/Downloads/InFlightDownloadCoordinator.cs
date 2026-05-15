using System.Collections.Concurrent;
using System.IO.Abstractions;
using Microsoft.Extensions.Logging;

namespace Octans.Core.Downloads;

public interface IInFlightDownloadCoordinator
{
    InFlightDownloadLease JoinOrStart(
        string deduplicationKey,
        string stagingPath,
        Func<string, CancellationToken, Task<SharedDownloadResult>> startTransfer);
}

public sealed class InFlightDownloadCoordinator(
    IFileSystem fileSystem,
    ILogger<InFlightDownloadCoordinator> logger) : IInFlightDownloadCoordinator
{
    private readonly ConcurrentDictionary<string, SharedDownload> _downloads = new(StringComparer.Ordinal);

    public InFlightDownloadLease JoinOrStart(
        string deduplicationKey,
        string stagingPath,
        Func<string, CancellationToken, Task<SharedDownloadResult>> startTransfer)
    {
        var shared = _downloads.GetOrAdd(
            deduplicationKey,
            key => new SharedDownload(
                key,
                stagingPath,
                startTransfer,
                fileSystem,
                logger,
                Remove));

        shared.AddParticipant();
        return new(shared);
    }

    private void Remove(string key, SharedDownload shared)
    {
        _downloads.TryRemove(new KeyValuePair<string, SharedDownload>(key, shared));
    }
}

public sealed class InFlightDownloadLease(SharedDownload shared) : IAsyncDisposable
{
    private bool _disposed;

    public Task<SharedDownloadResult> TransferTask => shared.TransferTask;

    public Task RunFinalizerAsync(Func<Task> finalizer, CancellationToken cancellationToken)
    {
        return shared.RunFinalizerAsync(finalizer, cancellationToken);
    }

    public void ParticipantCanceled()
    {
        shared.ParticipantCanceled();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await shared.ReleaseParticipantAsync();
    }
}

public sealed record SharedDownloadResult(
    string StagingPath,
    long BytesDownloaded,
    long TotalBytes,
    TimeSpan Elapsed,
    int HttpStatusCode,
    string? ResponseContentType,
    string? ResponseETag,
    DateTimeOffset? ResponseLastModified);

public sealed class SharedDownload : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly SemaphoreSlim _finalizationLock = new(1, 1);
    private readonly IFileSystem _fileSystem;
    private readonly ILogger _logger;
    private readonly Action<string, SharedDownload> _remove;
    private int _participantCount;
    private int _cleanupStarted;

    public SharedDownload(
        string key,
        string stagingPath,
        Func<string, CancellationToken, Task<SharedDownloadResult>> startTransfer,
        IFileSystem fileSystem,
        ILogger logger,
        Action<string, SharedDownload> remove)
    {
        Key = key;
        StagingPath = stagingPath;
        _fileSystem = fileSystem;
        _logger = logger;
        _remove = remove;
        TransferTask = Task.Run(() => startTransfer(stagingPath, _cancellation.Token));
    }

    public string Key { get; }
    public string StagingPath { get; }
    public Task<SharedDownloadResult> TransferTask { get; }

    public void AddParticipant()
    {
        Interlocked.Increment(ref _participantCount);
    }

    public void ParticipantCanceled()
    {
        if (Volatile.Read(ref _participantCount) <= 1)
        {
            _cancellation.Cancel();
        }
    }

    public async Task RunFinalizerAsync(Func<Task> finalizer, CancellationToken cancellationToken)
    {
        await _finalizationLock.WaitAsync(cancellationToken);
        try
        {
            await finalizer();
        }
        finally
        {
            _finalizationLock.Release();
        }
    }

    public async ValueTask ReleaseParticipantAsync()
    {
        if (Interlocked.Decrement(ref _participantCount) > 0)
        {
            return;
        }

        await CleanupAsync();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _cleanupStarted, 1) == 1)
        {
            return;
        }

        try
        {
            _cancellation.Cancel();
            if (_fileSystem.File.Exists(StagingPath))
            {
                _fileSystem.File.Delete(StagingPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispose shared download {StagingPath}", StagingPath);
        }
        finally
        {
            _remove(Key, this);
            _finalizationLock.Dispose();
            _cancellation.Dispose();
        }
    }

    private async ValueTask CleanupAsync()
    {
        if (Interlocked.Exchange(ref _cleanupStarted, 1) == 1)
        {
            return;
        }

        try
        {
            await _cancellation.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            if (_fileSystem.File.Exists(StagingPath))
            {
                _fileSystem.File.Delete(StagingPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete shared download staging file {StagingPath}", StagingPath);
        }
        finally
        {
            _remove(Key, this);
            _finalizationLock.Dispose();
            _cancellation.Dispose();
        }
    }
}
