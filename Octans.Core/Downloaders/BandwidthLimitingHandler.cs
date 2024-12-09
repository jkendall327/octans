namespace Octans.Core.Downloaders;

public class BandwidthLimitingHandler : DelegatingHandler
{
    private readonly BandwidthLimiter _limiter;

    public BandwidthLimitingHandler(BandwidthLimiter limiter, HttpMessageHandler? innerHandler = null)
        : base(innerHandler ?? new HttpClientHandler())
    {
        _limiter = limiter;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request.RequestUri, nameof(request));

        var domain = request.RequestUri.Host;
        var requestSize = request.Content?.Headers.ContentLength ?? 0;

        _limiter.EnsureCanMakeRequest(domain, requestSize);

        var response = await base.SendAsync(request, cancellationToken);

        _limiter.TrackUsage(domain, response.Content.Headers.ContentLength ?? 0);

        return response;
    }
}