# Octans downloader system

## Motivation and goals

- Queueable, pausable and resumable downloads
- Concurrent downloads with a configurable concurrency limit
- Visibility of download progress for UI purposes
- Simple fire-and-forget interface for calling code

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
    options.DefaultBytesPerSecond = 1024 * 1024; // 1 MB/s
});

builder.Services.AddDownloadManager(options =>
{
    options.MaxConcurrentDownloads = 5;
    options.MaxConcurrentDownloadsPerDomain = 2;
});
```

## Components

- `IDownloadService` accepts feature-level download requests and exposes cancel/pause/resume/retry commands.
- `IDownloadQueue` persists queued work and chooses the next bandwidth-eligible job.
- `DownloadBackgroundService` is registered by `AddDownloadManager` and runs the worker loop.
- `HttpDownloader` performs the HTTP request, streams bytes to disk, and reports progress.
- `IDownloadStateService` tracks active status in memory, persists state transitions, and raises UI notifications.
