using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octans.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Octans.Server;

public class ThumbnailCreationRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required byte[] Bytes { get; init; }
    public required HashedBytes Hashed { get; init; }
}

public class ThumbnailCreator
{
    private readonly IFileSystem _fileSystem;
    private readonly IOptions<GlobalSettings> _globalSettings;
    private readonly ILogger<ThumbnailCreator> _logger;

    public ThumbnailCreator(IFileSystem fileSystem, IOptions<GlobalSettings> globalSettings, ILogger<ThumbnailCreator> logger)
    {
        _fileSystem = fileSystem;
        _globalSettings = globalSettings;
        _logger = logger;
    }

    public async Task ProcessThumbnailRequestAsync(ThumbnailCreationRequest request, CancellationToken stoppingToken = default)
    {
        using var _ = _logger.BeginScope(new Dictionary<string, object>
        {
            ["RequestId"] = request.Id
        });

        _logger.LogInformation("Starting thumbnail creation");
        
        using var image = Image.Load(request.Bytes);
        
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new(200, 200),
            Mode = ResizeMode.Max
        }));

        var thumbnailBytes = await SaveThumbnailAsync(image, stoppingToken);

        _logger.LogDebug("Thumbnail generated at {ThumbnailSize} bytes", thumbnailBytes.Length);
        
        var destination = _fileSystem.Path.Join(_globalSettings.Value.AppRoot,
            "db",
            "files",
            request.Hashed.ThumbnailBucket,
            request.Hashed.Hexadecimal + ".jpeg");

        _logger.LogInformation("Writing thumbnail to {ThumbnailDestination}", destination);
        
        await _fileSystem.File.WriteAllBytesAsync(destination, thumbnailBytes, stoppingToken);
    }

    private async Task<byte[]> SaveThumbnailAsync(Image image, CancellationToken stoppingToken)
    {
        using var memoryStream = new MemoryStream();
        
        await image.SaveAsJpegAsync(memoryStream, stoppingToken);
        
        return memoryStream.ToArray();
    }
}