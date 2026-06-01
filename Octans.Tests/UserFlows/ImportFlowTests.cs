using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Octans.Client;
using Octans.Core;
using Octans.Core.Filesystem;
using Octans.Core.Importing;
using Octans.Core.Tags;
using Octans.Data.Models;
using Octans.Tests.Helpers;
using Xunit.Abstractions;

namespace Octans.Tests.UserFlows;

public sealed class ImportFlowTests(ITestOutputHelper output)
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
    public async Task UserCan_ImportMediaFromLocalFilesystem_AndThen_SeeItInTheirInbox()
    {
        await using var factory = new OctansApiFactory(output);
        var client = factory.CreateClient();
        var imageStorage = factory.Services.GetRequiredService<ImageStorage>();
        imageStorage.EnsureStorage();

        var source = factory.FileSystem.Path.Join(factory.AppRoot, "imports", "library-spine.jpg");
        factory.FileSystem.AddFile(source, new(TestingConstants.MinimalJpeg));

        var hash = ContentHash.FromContent(TestingConstants.MinimalJpeg);
        var request = new ImportJobCreateRequest
        {
            ImportType = ImportType.File,
            Sources = [source],
            TagsBySource = new Dictionary<string, ICollection<TagModel>>
            {
                [source] = [new("series", "octans smoke test")]
            }
        };

        var createResponse = await client.PostAsJsonAsync(
            new Uri("/import-jobs", UriKind.Relative),
            request,
            JsonOptions);
        var created = await createResponse.Content.ReadFromJsonAsync<ImportJobCreatedDto>(JsonOptions);

        var processor = new ImportProcessorService(
            factory.Services,
            NullLogger<ImportProcessorService>.Instance);
        var processedJob = await processor.ProcessQueuedJob();

        var job = await client.GetFromJsonAsync<ImportJobDto>(
            new Uri($"/import-jobs/{created!.JobId}", UriKind.Relative),
            JsonOptions);
        var queryResponse = await client.PostAsJsonAsync(
            new Uri("/files/query", UriKind.Relative),
            InboxQuery,
            JsonOptions);
        var inboxResults = await queryResponse.Content.ReadFromJsonAsync<List<HashItem>>(JsonOptions);
        var detailsResponse = await client.GetAsync(new Uri($"/media/{hash.Hex}/details", UriKind.Relative));
        var details = await detailsResponse.Content.ReadFromJsonAsync<MediaDetailsDto>(JsonOptions);
        var mediaResponse = await client.GetAsync(new Uri($"/media/{hash.Hex}", UriKind.Relative));
        var mediaBytes = await mediaResponse.Content.ReadAsByteArrayAsync();

        using var _ = new AssertionScope();

        createResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        createResponse.Headers.Location?.OriginalString.Should().Be($"/import-jobs/{created.JobId}");
        processedJob.Should().BeTrue("the real import processor should pick up the queued API-created job");

        job.Should().NotBeNull();
        job!.Status.Should().Be("Completed");
        job.TotalItems.Should().Be(1);
        job.ProcessedItems.Should().Be(1);
        job.FailedItems.Should().Be(0);
        job.Items.Should().ContainSingle();
        job.Items.Single().Status.Should().Be("Completed");
        job.Items.Single().Source.Should().Be(source);

        queryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        inboxResults.Should().ContainSingle(item => item.Hash.SequenceEqual(hash.Bytes));

        detailsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        details.Should().NotBeNull();
        details!.Hash.Should().Be(hash.Hex);
        details.Repository.Should().Be(RepositoryType.Inbox);
        details.Extension.Should().Be("jpg");
        details.ContentType.Should().Be("image/jpeg");
        details.MediaUrl.Should().Be($"/media/{hash.Hex}");
        details.Tags.Should().ContainSingle(tag =>
            tag.Namespace == "series" && tag.Subtag == "octans smoke test");
        details.Notes.Should().BeEmpty();

        mediaResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        mediaResponse.Content.Headers.ContentType?.MediaType.Should().Be("image/jpeg");
        mediaBytes.Should().Equal(TestingConstants.MinimalJpeg);
    }
}
