using System.Net;
using System.Net.Http.Json;

namespace Octans.Tests;

public class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly object? _responseContent;

    public FakeHttpMessageHandler(HttpStatusCode statusCode, object? responseContent = null)
    {
        _statusCode = statusCode;
        _responseContent = responseContent;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage
        {
            StatusCode = _statusCode,
            Content = JsonContent.Create(_responseContent)
        });
    }
}