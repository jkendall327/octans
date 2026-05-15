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
```

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

## Components

- `IDownloadService` accepts feature-level download requests and exposes cancel/pause/resume/retry commands.
- `IDownloadStateService` owns atomic status-plus-queue transitions for new, paused, canceled, resumed, and retried downloads.
- `IDownloadQueue` restores queued work and chooses the next job by priority, queued time, and saturated-domain exclusions.
- `DownloadBackgroundService` is registered by `AddDownloadManager` and runs the worker loop.
- `HttpDownloader` performs the HTTP request, waits for byte-level bandwidth budget while streaming, writes bytes to disk, and reports progress.
- `IDownloadStateService` tracks active status in memory, persists state transitions, and raises UI notifications.

Bandwidth limiting is byte-aware. The queue does not block future downloads
based on completed download totals; active streams are paced through
`IDownloadBandwidthGate`.
