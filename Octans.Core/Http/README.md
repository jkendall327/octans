# Octans downloader system

## Motivation and goals

- Queueable, pausable and resumable downloads
- Concurrent downloads with a configurable concurrency limit
- Visibility of download progress for UI purposes
- Simple fire-and-forget interface for calling code

Pause/resume is intentionally scoped to stopping an active transfer and
re-queueing it from the beginning. Byte-level HTTP resume with `Range` requests,
partial-file validation, and ETag/Last-Modified handling is explicitly out of
scope for now.

## Usage

```csharp
var service = serviceProvider.GetRequiredService<IDownloadService>();

await service.QueueDownloadAsync(new()
{
    Url = new("https://upload.wikimedia.org/wikipedia/commons/d/de/Nokota_Horses_cropped.jpg"),
    DestinationPath = "/home/janedoe/Downloads/horse.jpg"
});

var result = await service.QueueDownloadAndWaitAsync(new()
{
    Url = new("https://example.com/image.jpg"),
    DestinationPath = "/tmp/image.jpg"
});
```

`QueueDownloadAndWaitAsync` keeps the queue-based subsystem underneath, but gives
callers a sequential async workflow when they need the completed file before
continuing. It wakes from in-process download state notifications and falls back
to polling the durable result store, using
`DownloadManagerOptions.CompletionPollingInterval`.

## Setup

```csharp
builder.Services.AddBandwidthLimiter(options =>
{
    options.GlobalBytesPerSecond = 5 * 1024 * 1024; // 5 MB/s total
    options.DefaultBytesPerSecond = 1024 * 1024; // 1 MB/s
});

builder.Services.AddDownloadManager(options =>
{
    options.MaxConcurrentDownloads = 5;
    options.MaxConcurrentDownloadsPerDomain = 2;
    options.CompletionPollingInterval = TimeSpan.FromSeconds(2);
    options.DiskSpace.RequiredFreeSpaceHeadroomBytes = 250L * 1024 * 1024;
    options.SizeLimits.MaxBytes = 10L * 1024 * 1024 * 1024;
    options.ContentTypeValidation.AllowMissingContentType = true;
    options.ContentTypeValidation.AllowGenericContentType = true;
});
```

`DownloadRequest.AllowedContentTypes` can narrow a download to content types a
feature is prepared to handle, such as `image/*`. When a request does not
specify allowed content types, the downloader infers best-effort expectations
from common destination extensions. Missing and generic response content types
are allowed by default because many file hosts are imprecise, and these defaults
can be changed through `DownloadManagerOptions` or the `Downloads` configuration
section.

Downloads also perform best-effort disk-space checks before known-size transfers
and while streaming chunks. `DownloadManagerOptions.DiskSpace` controls whether
the checks run and how much free-space headroom remains reserved on the
destination volume.

Downloads enforce `DownloadManagerOptions.SizeLimits.MaxBytes` before streaming
known oversized responses and while streaming unknown or misreported response
bodies. Domain and source-type entries can override the global cap for specific
hosts or callers.

## Components

- `IDownloadService` accepts feature-level download requests, exposes cancel/pause/resume/retry commands, and can wait asynchronously for terminal job results.
- `IDownloadStateService` owns atomic status-plus-queue transitions for new, paused, canceled, resumed, and retried downloads.
- `IDownloadQueue` restores queued work and chooses the next job by priority, queued time, and saturated-domain exclusions.
- `DownloadBackgroundService` is registered by `AddDownloadManager` and runs the worker loop.
- `HttpDownloader` performs the HTTP request, waits for byte-level bandwidth budget while streaming, writes bytes to disk, and reports progress.
- `IDownloadStateService` tracks active status in memory, persists state transitions, and raises UI notifications.

Bandwidth limiting is byte-aware. The queue does not block future downloads
based on completed download totals; active streams are paced through
`IDownloadBandwidthGate`.
