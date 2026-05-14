using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Octans.Core.Filesystem;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Octans.Core.Thumbnails;

public class ThumbnailCreator(
    IFileSystem fileSystem,
    ImageStorage imageStorage,
    ILogger<ThumbnailCreator> logger)
{
    public async Task ProcessThumbnailRequestAsync(ThumbnailCreationRequest request, CancellationToken stoppingToken = default)
    {
        using var _ = logger.BeginScope(new Dictionary<string, object>
        {
            ["RequestId"] = request.Id
        });

        logger.LogInformation("Starting thumbnail creation");

        using var image = Image.Load(request.Bytes);

        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new(200, 200),
            Mode = ResizeMode.Max
        }));

        var thumbnailBytes = await SaveThumbnailAsync(image, stoppingToken);

        logger.LogDebug("Thumbnail generated at {ThumbnailSize} bytes", thumbnailBytes.Length);

        var destination = imageStorage.GetThumbnailDestination(request.Hash);

        logger.LogInformation("Writing thumbnail to {ThumbnailDestination}", destination);

        await fileSystem.File.WriteAllBytesAsync(destination, thumbnailBytes, stoppingToken);
    }

    private async Task<byte[]> SaveThumbnailAsync(Image image, CancellationToken stoppingToken)
    {
        using var memoryStream = new MemoryStream();

        await image.SaveAsJpegAsync(memoryStream, stoppingToken);

        return memoryStream.ToArray();
    }
}
