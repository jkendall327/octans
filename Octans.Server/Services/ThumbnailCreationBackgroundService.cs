using System.IO.Abstractions;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Octans.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Octans.Server;

public class ThumbnailCreationRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required byte[] Bytes { get; set; }
    public required HashedBytes Hashed { get; set; }
}

public class ThumbnailCreationBackgroundService : BackgroundService
{
    private readonly ChannelReader<ThumbnailCreationRequest> _channel;
    private readonly IOptions<GlobalSettings> _globalSettings;
    private readonly IFile _file;
    private readonly IPath _path;
    private readonly ILogger<ThumbnailCreationBackgroundService> _logger;

    public ThumbnailCreationBackgroundService(ChannelReader<ThumbnailCreationRequest> channel,
        IFile file,
        IPath path,
        IOptions<GlobalSettings> globalSettings,
        ILogger<ThumbnailCreationBackgroundService> logger)
    {
        _channel = channel;
        _logger = logger;
        _globalSettings = globalSettings;
        _path = path;
        _file = file;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach(var request in _channel.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessThumbnailRequestAsync(request, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing thumbnail request");
            }
        }
    }
    
    private async Task ProcessThumbnailRequestAsync(ThumbnailCreationRequest request, CancellationToken stoppingToken)
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
        
        var destination = _path.Join(_globalSettings.Value.AppRoot,
            "db",
            "files",
            request.Hashed.ThumbnailBucket,
            request.Hashed.Hexadecimal + ".jpeg");
        
        _logger.LogInformation("Writing thumbnail to {ThumbnailDestination}", destination);
        
        await _file.WriteAllBytesAsync(destination, thumbnailBytes, stoppingToken);
    }

    private async Task<byte[]> SaveThumbnailAsync(Image image, CancellationToken stoppingToken)
    {
        using var memoryStream = new MemoryStream();
        
        await image.SaveAsJpegAsync(memoryStream, stoppingToken);
        
        return memoryStream.ToArray();
    }
}