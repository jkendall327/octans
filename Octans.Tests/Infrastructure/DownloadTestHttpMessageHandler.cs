using System.Collections.ObjectModel;
using System.Net;

namespace Octans.Tests.Infrastructure;

internal sealed class DownloadTestHttpMessageHandler : HttpMessageHandler
{
    public HttpResponseMessage? ResponseToReturn { get; set; }
    public Exception? ExceptionToThrow { get; set; }
    public TimeSpan DelayBeforeResponse { get; set; } = TimeSpan.Zero;
    public bool WaitForCancellation { get; set; }
    public TaskCompletionSource RequestStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Collection<HttpRequestMessage> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestStarted.TrySetResult();

        if (WaitForCancellation)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        if (DelayBeforeResponse > TimeSpan.Zero)
        {
            await Task.Delay(DelayBeforeResponse, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (ExceptionToThrow != null)
        {
            throw ExceptionToThrow;
        }

        return ResponseToReturn ?? new HttpResponseMessage(HttpStatusCode.OK);
    }
}
