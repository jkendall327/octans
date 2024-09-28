using System.IO.Abstractions;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Octans.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Octans.Server;



public class ThumbnailCreationBackgroundService : BackgroundService
{
    private readonly ChannelReader<ThumbnailCreationRequest> _channel;
    private readonly ILogger<ThumbnailCreationBackgroundService> _logger;
    private readonly ThumbnailCreator _thumbnailCreator;

    public ThumbnailCreationBackgroundService(
        ThumbnailCreator creator,
        ChannelReader<ThumbnailCreationRequest> channel,
        ILogger<ThumbnailCreationBackgroundService> logger)
    {
        _thumbnailCreator = creator;
        _channel = channel;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _channel.ReadAllAsync(stoppingToken))
        {
            try
            {
                await _thumbnailCreator.ProcessThumbnailRequestAsync(request, stoppingToken);
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
}