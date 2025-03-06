using System.Collections.Concurrent;
using System.Threading.Channels;
using Octans.Core.Downloaders;

namespace Octans.Server;

public record DownloadRequest(Uri Uri, long Bytes);

public class DownloadBackgroundService : BackgroundService
{
    private readonly BandwidthLimiter _limiter;
    private readonly ChannelReader<DownloadRequest> _queue;
    private readonly IHttpClientFactory _factory;
    private readonly ConcurrentDictionary<DownloadRequest, bool> _inflight = [];

    public DownloadBackgroundService(ChannelReader<DownloadRequest> queue, BandwidthLimiter limiter, IHttpClientFactory factory)
    {
        _queue = queue;
        _limiter = limiter;
        _factory = factory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var client = _factory.CreateClient();

        await foreach (var request in _queue.ReadAllAsync(stoppingToken))
        {
            var ok = _limiter.CanMakeRequest(request.Uri.ToString(), request.Bytes);

            if (ok)
            {
                // await client.GetAsync();
            }
            else
            {
                _inflight.AddOrUpdate(request, downloadRequest => false, (u, b) => false);
            }
        }
    }
}