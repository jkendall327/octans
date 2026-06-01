using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Octans.Core.Http;
using Octans.Core.Http.Models;

namespace Octans.Tests.Downloads;

public class HttpDocumentFetcherTests
{
    [Fact]
    public async Task GetStringAsync_ShouldApplySharedHeadersAndReturnContent()
    {
        HttpRequestMessage? observedRequest = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            observedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("hello", Encoding.UTF8, "text/html")
            };
        }));
        var downloadOptions = new DownloadManagerOptions();
        downloadOptions.RequestHeaders.DefaultUserAgent = "Octans-Test/1.0";
        downloadOptions.RequestHeaders.Headers["X-Octans-Test"] = "yes";
        var sut = CreateFetcher(httpClient, downloadOptions);

        var result = await sut.GetStringAsync(new("https://example.com/document"));

        result.Should().Be("hello");
        observedRequest.Should().NotBeNull();
        observedRequest!.Headers.GetValues("User-Agent").Should().Contain("Octans-Test/1.0");
        observedRequest.Headers.GetValues("X-Octans-Test").Should().Contain("yes");
    }

    [Fact]
    public async Task GetStringAsync_ShouldRejectReportedResponseLargerThanConfiguredLimit()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("too large"))
        }));
        var sut = CreateFetcher(httpClient, documentOptions: new() { MaxResponseBytes = 4 });

        var act = async () => await sut.GetStringAsync(new("https://example.com/document"));

        await act.Should()
            .ThrowAsync<HttpDocumentFetchException>()
            .WithMessage("*reported*exceeding*4 byte limit*");
    }

    [Fact]
    public async Task GetStringAsync_ShouldRejectFailedStatusCode()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var sut = CreateFetcher(httpClient);

        var act = async () => await sut.GetStringAsync(new("https://example.com/document"));

        await act.Should()
            .ThrowAsync<HttpDocumentFetchException>()
            .WithMessage("*HTTP 500*");
    }

    private static HttpDocumentFetcher CreateFetcher(
        HttpClient httpClient,
        DownloadManagerOptions? downloadOptions = null,
        HttpDocumentFetcherOptions? documentOptions = null)
    {
        var clientFactory = Substitute.For<IHttpClientFactory>();
        clientFactory.CreateClient("DownloadClient").Returns(httpClient);
        var requestHeaderProvider = new DownloadRequestHeaderProvider(Options.Create(downloadOptions ?? new()));

        return new(
            clientFactory,
            requestHeaderProvider,
            Options.Create(documentOptions ?? new()),
            NullLogger<HttpDocumentFetcher>.Instance);
    }
}
