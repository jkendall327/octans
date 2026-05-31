using System.Net;

namespace Octans.Tests.Infrastructure;

internal sealed class DownloadManagerHttpMessageHandler : HttpMessageHandler
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Uri, byte[]> _responses = new();
    private readonly Dictionary<Uri, Queue<Func<HttpResponseMessage>>> _responseSequences = new();
    private readonly Dictionary<Uri, int> _requestCounts = new();
    private readonly Dictionary<string, int> _activeRequestsByHost = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _maxActiveRequestsByHost = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Uri> _startedRequests = [];
    private readonly TaskCompletionSource _releaseResponses =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _activeRequestCount;

    public bool PauseBeforeResponding { get; set; }

    public int ActiveRequestCount
    {
        get
        {
            lock (_lock)
            {
                return _activeRequestCount;
            }
        }
    }

    public IReadOnlyCollection<Uri> StartedRequests
    {
        get
        {
            lock (_lock)
            {
                return _startedRequests.ToArray();
            }
        }
    }

    public void AddResponse(string url, string body)
    {
        _responses[new(url)] = System.Text.Encoding.UTF8.GetBytes(body);
    }

    public void AddResponse(string url, HttpStatusCode statusCode)
    {
        AddResponseSequence(url, () => new(statusCode));
    }

    public void AddResponseSequence(string url, params Func<HttpResponseMessage>[] responses)
    {
        _responseSequences[new(url)] = new(responses);
    }

    public int MaxActiveRequestsForHost(string host)
    {
        lock (_lock)
        {
            return _maxActiveRequestsByHost.GetValueOrDefault(host);
        }
    }

    public int RequestCountFor(Uri uri)
    {
        lock (_lock)
        {
            return _requestCounts.GetValueOrDefault(uri);
        }
    }

    public void ReleaseResponses()
    {
        _releaseResponses.TrySetResult();
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var requestUri = request.RequestUri ?? throw new InvalidOperationException("Request URI is required.");
        TrackRequestStarted(requestUri);

        try
        {
            if (PauseBeforeResponding)
            {
                await _releaseResponses.Task.WaitAsync(cancellationToken);
            }

            if (!_responses.TryGetValue(requestUri, out var body))
            {
                if (_responseSequences.TryGetValue(requestUri, out var sequence) && sequence.TryDequeue(out var next))
                {
                    return next();
                }

                return new(HttpStatusCode.NotFound);
            }

            return new(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body)
            };
        }
        finally
        {
            TrackRequestFinished(requestUri.Host);
        }
    }

    private void TrackRequestStarted(Uri requestUri)
    {
        lock (_lock)
        {
            _activeRequestCount++;
            _requestCounts.TryGetValue(requestUri, out var requestCount);
            _requestCounts[requestUri] = requestCount + 1;
            _startedRequests.Add(requestUri);
            _activeRequestsByHost.TryGetValue(requestUri.Host, out var hostCount);
            hostCount++;
            _activeRequestsByHost[requestUri.Host] = hostCount;
            _maxActiveRequestsByHost.TryGetValue(requestUri.Host, out var maxHostCount);
            _maxActiveRequestsByHost[requestUri.Host] = Math.Max(maxHostCount, hostCount);
        }
    }

    private void TrackRequestFinished(string host)
    {
        lock (_lock)
        {
            _activeRequestCount--;
            _activeRequestsByHost.TryGetValue(host, out var hostCount);
            if (hostCount <= 1)
            {
                _activeRequestsByHost.Remove(host);
                return;
            }

            _activeRequestsByHost[host] = hostCount - 1;
        }
    }
}
