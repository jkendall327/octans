using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Octans.Client;
using Octans.Core.Importing;
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

            return StubHttpResponses.Json("""{"version":"1.2.3"}""");
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

            return StubHttpResponses.Json("""[{"id":7,"hash":"AQID"}]""");
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

    [Fact]
    public async Task ImportFilesAsync_ReturnsCreatedImportJob()
    {
        var jobId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        HttpRequestMessage? observedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;

            return StubHttpResponses.Json($$"""{"jobId":"{{jobId}}"}""");
        });

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://octans.test")
        };

        var client = new OctansClient(httpClient);

        var result = await client.ImportFilesAsync(new()
        {
            ImportType = ImportType.File,
            DeleteAfterImport = false,
            Items = [new() { Filepath = "/images/test.jpg" }]
        });

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
            .Be(new Uri("https://octans.test/files"));

        result
            .Should()
            .Be(new ImportJobClientResult(jobId, $"/import-jobs/{jobId}"));
    }
}
