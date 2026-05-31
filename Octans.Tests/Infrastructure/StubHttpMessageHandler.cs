using System.Net;
using System.Text;

namespace Octans.Tests.Infrastructure;

internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    : HttpMessageHandler
{
    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : this((request, _) => Task.FromResult(respond(request)))
    {
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        respond(request, cancellationToken);
}

internal sealed class StaticHttpMessageHandler(string content) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage
        {
            Content = new StringContent(content)
        });
}

internal sealed class CancellableHttpMessageHandler : HttpMessageHandler
{
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WaitUntilStarted() => _started.Task.WaitAsync(TimeSpan.FromSeconds(5));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        _started.SetResult();
        await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);

        return new()
        {
            Content = new StringContent("<html></html>")
        };
    }
}

internal static class StubHttpResponses
{
    public static HttpResponseMessage Json(string content)
    {
        return new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
    }
}
