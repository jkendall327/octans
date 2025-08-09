using System.Net;
using System.Net.Http.Json;

namespace Octans.Tests;

public class FakeHttpMessageHandler(HttpStatusCode statusCode, object? responseContent = null) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage
        {
            StatusCode = statusCode,
            Content = JsonContent.Create(responseContent)
        });
    }
}