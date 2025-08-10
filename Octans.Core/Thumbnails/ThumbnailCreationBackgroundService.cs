using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Octans.Server;

public sealed class ThumbnailCreationBackgroundService(
    ThumbnailCreator creator,
    ChannelReader<ThumbnailCreationRequest> channel,
    ILogger<ThumbnailCreationBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in channel.ReadAllAsync(stoppingToken))
        {
            try
            {
                await creator.ProcessThumbnailRequestAsync(request, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing thumbnail request");
            }
        }
    }
}