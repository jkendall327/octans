using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Octans.Client;
using Octans.Data.Models;

namespace Octans.Tests.Infrastructure;

public class OctansClientTests
{
    [Fact]
    public async Task AddOctansClient_RegistersTypedClient()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            request
                .RequestUri
                .Should()
                .Be(new Uri("https://octans.test/version"));

            return JsonResponse("""{"version":"1.2.3"}""");
        });

        var services = new ServiceCollection();

        services
            .AddOctansClient(client => client.BaseAddress = new Uri("https://octans.test"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        await using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IOctansClient>();

        var version = await client.GetVersionAsync();

        version
            .Version
            .Should()
            .Be("1.2.3");
    }

    [Fact]
    public async Task QueryFilesAsync_PostsQueriesToEndpoint()
    {
        HttpRequestMessage? observedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;

            return JsonResponse("""[{"id":7,"hash":"AQID"}]""");
        });

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://octans.test")
        };

        var client = new OctansClient(httpClient);

        var files = await client.QueryFilesAsync(["rating:safe", "tag:cat"]);

        observedRequest
            .Should()
            .NotBeNull();

        observedRequest!
            .Method
            .Should()
            .Be(HttpMethod.Post);

        observedRequest
            .RequestUri
            .Should()
            .Be(new Uri("https://octans.test/files/query"));

        var body = await observedRequest.Content!.ReadAsStringAsync();

        body
            .Should()
            .Be("""["rating:safe","tag:cat"]""");

        files
            .Should()
            .ContainSingle()
            .Which
            .Should()
            .BeEquivalentTo(new HashItem
            {
                Id = 7,
                Hash = [1, 2, 3]
            });
    }

    [Fact]
    public void GetMediaUrl_NormalizesHashUrl()
    {
        var client = new OctansClient(new HttpClient());

        var url = client.GetMediaUrl("deadbeef");

        url
            .Should()
            .Be("/media/DEADBEEF");
    }

    private static HttpResponseMessage JsonResponse(string content)
    {
        return new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(respond(request));
        }
    }
}
