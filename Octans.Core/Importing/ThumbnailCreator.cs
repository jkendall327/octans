using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octans.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Octans.Server;

[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "DTO")]
public record ThumbnailCreationRequest(byte[] Bytes, HashedBytes Hashed)
{
    public Guid Id { get; set; } = Guid.NewGuid();
}

public class ThumbnailCreator(
    IFileSystem fileSystem,
    IOptions<GlobalSettings> globalSettings,
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

        var destination = fileSystem.Path.Join(globalSettings.Value.AppRoot,
            "db",
            "files",
            request.Hashed.ThumbnailBucket,
            request.Hashed.Hexadecimal + ".jpeg");

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