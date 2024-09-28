namespace Octans.Core.Downloaders;

using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class BandwidthLimitingHandler : DelegatingHandler
{
    private readonly ConcurrentDictionary<string, DomainBandwidthUsage> _domainUsage = new();

    public BandwidthLimitingHandler(HttpMessageHandler? innerHandler = null) : base(innerHandler ?? new HttpClientHandler())
    {
    }

    public void SetBandwidthLimit(string domain, long bytesPerHour)
    {
        _domainUsage.AddOrUpdate(domain,
            _ => new(bytesPerHour),
            (_, existing) =>
            {
                existing.BytesPerHour = bytesPerHour;
                return existing;
            });
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var domain = request.RequestUri.Host;
        
        if (!_domainUsage.TryGetValue(domain, out var usage))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        if (!usage.CanMakeRequest(request.Content?.Headers.ContentLength ?? 0))
        {
            throw new HttpRequestException($"Bandwidth limit exceeded for domain: {domain}");
        }

        var response = await base.SendAsync(request, cancellationToken);

        // Update bandwidth usage after successful request
        usage.AddUsage(response.Content.Headers.ContentLength ?? 0);

        return response;
    }
}