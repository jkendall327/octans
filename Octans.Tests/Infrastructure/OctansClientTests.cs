using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Octans.Client;
using Octans.Core.Importing;
using Octans.Data.Models;
using Octans.Data.Models.Duplicates;

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

    [Fact]
    public async Task ScanDuplicatesAsync_PostsToEndpoint()
    {
        HttpRequestMessage? observedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;

            return StubHttpResponses.Json("""{"perceptualHashesCalculated":2,"candidatesCreated":1}""");
        });

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://octans.test")
        };

        var client = new OctansClient(httpClient);

        var result = await client.ScanDuplicatesAsync();

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
            .Be(new Uri("https://octans.test/duplicates/scan"));

        result
            .Should()
            .Be(new DuplicateScanResultDto(2, 1));
    }

    [Fact]
    public async Task GetDuplicateCandidatesAsync_GetsCandidatesEndpoint()
    {
        HttpRequestMessage? observedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;

            return StubHttpResponses.Json(
                """
                [{"id":5,"hashId1":7,"hash1":"AAAA","mediaUrl1":"/media/AAAA","hashId2":8,"hash2":"BBBB","mediaUrl2":"/media/BBBB","distance":4}]
                """);
        });

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://octans.test")
        };

        var client = new OctansClient(httpClient);

        var candidates = await client.GetDuplicateCandidatesAsync();

        observedRequest
            .Should()
            .NotBeNull();

        observedRequest!
            .Method
            .Should()
            .Be(HttpMethod.Get);

        observedRequest
            .RequestUri
            .Should()
            .Be(new Uri("https://octans.test/duplicates/candidates"));

        candidates
            .Should()
            .ContainSingle()
            .Which
            .Should()
            .Be(new DuplicateCandidateDto(5, 7, "AAAA", "/media/AAAA", 8, "BBBB", "/media/BBBB", 4));
    }

    [Fact]
    public async Task ResolveDuplicateCandidateAsync_PostsResolution()
    {
        HttpRequestMessage? observedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;

            return new(HttpStatusCode.NoContent);
        });

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://octans.test")
        };

        var client = new OctansClient(httpClient);

        await client.ResolveDuplicateCandidateAsync(5, DuplicateResolution.KeepBoth, 7);

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
            .Be(new Uri("https://octans.test/duplicates/candidates/5/resolution"));

        var body = await observedRequest.Content!.ReadAsStringAsync();

        body
            .Should()
            .Be("""{"resolution":"KeepBoth","keepHashId":7}""");
    }
}
