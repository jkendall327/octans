using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Octans.Client;
using Octans.Core;
using Octans.Core.Http;
using Octans.Core.Importing;
using Octans.Data.Models;
using Octans.Tests.Helpers;
using Xunit.Abstractions;

namespace Octans.Tests.UserFlows;

public sealed class DownloadFlowTests(ITestOutputHelper output)
{
    private static readonly string[] InboxQuery = ["system:inbox"];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    [Fact]
    public async Task UserCan_ImportRawUrlThroughDurableDownloadJob_AndThen_UseItFromLibrary()
    {
        var remoteUrl = new Uri("https://raw-url-import.test/images/octans-flow.jpg");
        var remoteHttp = new DownloadTestHttpMessageHandler
        {
            ResponseToReturn = CreateImageResponse(TestingConstants.MinimalJpeg)
        };

        await using var factory = CreateFactory(remoteHttp);
        factory.FileSystem.AddDrive("/", new()
        {
            AvailableFreeSpace = 1024 * 1024 * 1024,
            TotalFreeSpace = 1024 * 1024 * 1024,
            TotalSize = 1024L * 1024 * 1024 * 10
        });

        var client = factory.CreateClient();
        var hash = ContentHash.FromContent(TestingConstants.MinimalJpeg);
        var request = new ImportJobCreateRequest
        {
            ImportType = ImportType.RawUrl,
            Sources = [remoteUrl.ToString()]
        };

        var createResponse = await client.PostAsJsonAsync(
            new Uri("/import-jobs", UriKind.Relative),
            request,
            JsonOptions);
        var created = await createResponse.Content.ReadFromJsonAsync<ImportJobCreatedDto>(JsonOptions);

        var processor = new ImportProcessorService(
            factory.Services,
            NullLogger<ImportProcessorService>.Instance);
        var processedJob = await processor
            .ProcessQueuedJob(new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token)
            .WaitAsync(TimeSpan.FromSeconds(15));

        var job = await client.GetFromJsonAsync<ImportJobDto>(
            new Uri($"/import-jobs/{created!.JobId}", UriKind.Relative),
            JsonOptions);
        var inboxResults = await Query(client, InboxQuery);
        var detailsResponse = await client.GetAsync(new Uri($"/media/{hash.Hex}/details", UriKind.Relative));
        var details = await detailsResponse.Content.ReadFromJsonAsync<MediaDetailsDto>(JsonOptions);
        var mediaResponse = await client.GetAsync(new Uri($"/media/{hash.Hex}", UriKind.Relative));
        var mediaBytes = await mediaResponse.Content.ReadAsByteArrayAsync();
        var download = await GetRawUrlDownload(factory, remoteUrl);
        var source = remoteUrl.ToString();

        using (new AssertionScope("The RawUrl import job is accepted and processed"))
        {
            createResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
            createResponse.Headers.Location?.OriginalString.Should().Be($"/import-jobs/{created.JobId}");
            processedJob.Should().BeTrue("the real import processor should pick up the queued API-created RawUrl job");
        }

        using (new AssertionScope("The import job records durable item status"))
        {
            job.Should().NotBeNull();
            job.Should().Match<ImportJobDto>(j =>
                j.Status == "Completed"
                && j.TotalItems == 1
                && j.ProcessedItems == 1
                && j.FailedItems == 0);

            var item = job.Items.Should().ContainSingle().Which;
            item.Should().Match<ImportJobItemDto>(i =>
                i.ImportType == nameof(ImportType.RawUrl)
                && i.Status == "Completed"
                && i.Source == source);
        }

        using (new AssertionScope("The remote bytes crossed the durable download boundary"))
        {
            remoteHttp.Requests.Should().ContainSingle(requestMessage => requestMessage.RequestUri == remoteUrl);
            download.Should().Match<DownloadStatus>(d =>
                d.State == DownloadState.Completed
                && d.TerminalOutcome == DownloadTerminalOutcome.Completed
                && d.HttpStatusCode == (int)HttpStatusCode.OK
                && d.ResponseContentType == "image/jpeg"
                && d.BytesDownloaded == TestingConstants.MinimalJpeg.Length
                && d.SourceType == nameof(ImportType.RawUrl)
                && d.SourceId == source
                && d.CompletedAt != null);
        }

        using (new AssertionScope("The imported image is usable through normal library APIs"))
        {
            inboxResults.Response.StatusCode.Should().Be(HttpStatusCode.OK);
            inboxResults.Items.Should().ContainSingle(item => item.Hash.SequenceEqual(hash.Bytes));

            detailsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            details.Should().NotBeNull();
            details.Should().Match<MediaDetailsDto>(d =>
                d.Hash == hash.Hex
                && d.Repository == RepositoryType.Inbox
                && d.ContentType == "image/jpeg"
                && d.MediaUrl == $"/media/{hash.Hex}");

            mediaResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            mediaResponse.Content.Headers.ContentType?.MediaType.Should().Be("image/jpeg");
            mediaBytes.Should().Equal(TestingConstants.MinimalJpeg);
        }
    }

    private OctansApiFactory CreateFactory(DownloadTestHttpMessageHandler remoteHttp) =>
        new(output, services =>
        {
            services.AddSingleton<IHostedService, DownloadBackgroundService>();
            services.AddHttpClient("DownloadClient")
                .ConfigurePrimaryHttpMessageHandler(() => remoteHttp);
        });

    private static HttpResponseMessage CreateImageResponse(byte[] bytes)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");

        return response;
    }

    private static async Task<QueryResult> Query(HttpClient client, string[] query)
    {
        var response = await client.PostAsJsonAsync(
            new Uri("/files/query", UriKind.Relative),
            query,
            JsonOptions);

        var items = await response.Content.ReadFromJsonAsync<List<HashItem>>(JsonOptions);

        return new(response, items ?? []);
    }

    private static async Task<DownloadStatus> GetRawUrlDownload(OctansApiFactory factory, Uri remoteUrl)
    {
        await using var scope = factory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();

        return await db.DownloadStatuses.SingleAsync(download => download.Url == remoteUrl.ToString());
    }

    private sealed record QueryResult(HttpResponseMessage Response, IReadOnlyList<HashItem> Items);
}
