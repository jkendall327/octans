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
});

builder.Services.AddHostedService<DownloadManager>();
```

## Components

TBD