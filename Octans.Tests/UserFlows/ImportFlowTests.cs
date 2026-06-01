using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Octans.Client;
using Octans.Core;
using Octans.Core.Filesystem;
using Octans.Core.Importing;
using Octans.Core.Notes;
using Octans.Core.Repositories;
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
        var imported = await ImportLocalImage(
            factory,
            client,
            "library-spine.jpg",
            TestingConstants.MinimalJpeg,
            [new("series", "octans smoke test")]);

        var job = await client.GetFromJsonAsync<ImportJobDto>(
            new Uri($"/import-jobs/{imported.Created.JobId}", UriKind.Relative),
            JsonOptions);
        var inboxResults = await Query(client, InboxQuery);
        var detailsResponse = await client.GetAsync(
            new Uri($"/media/{imported.Hash.Hex}/details", UriKind.Relative));
        var details = await detailsResponse.Content.ReadFromJsonAsync<MediaDetailsDto>(JsonOptions);
        var mediaResponse = await client.GetAsync(new Uri($"/media/{imported.Hash.Hex}", UriKind.Relative));
        var mediaBytes = await mediaResponse.Content.ReadAsByteArrayAsync();

        using (new AssertionScope("The import job is accepted and processed"))
        {
            imported.CreateResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
            imported.CreateResponse.Headers.Location?.OriginalString.Should().Be($"/import-jobs/{imported.Created.JobId}");
            imported.ProcessedJob.Should().BeTrue("the real import processor should pick up the queued API-created job");
        }

        using (new AssertionScope("The import job records a completed item"))
        {
            job.Should().NotBeNull();
            job.Should().Match<ImportJobDto>(j =>
                j.Status == "Completed"
                && j.TotalItems == 1
                && j.ProcessedItems == 1
                && j.FailedItems == 0);

            var item = job.Items.Should().ContainSingle().Which;
            item.Should().Match<ImportJobItemDto>(i =>
                i.Status == "Completed"
                && i.Source == imported.Source);
        }

        using (new AssertionScope("The imported media appears in inbox search"))
        {
            inboxResults.Response.StatusCode.Should().Be(HttpStatusCode.OK);
            inboxResults.Items.Should().ContainSingle(item => item.Hash.SequenceEqual(imported.Hash.Bytes));
        }

        using (new AssertionScope("The media details expose the imported metadata"))
        {
            detailsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            details.Should().NotBeNull();
            details.Should().Match<MediaDetailsDto>(d =>
                d.Hash == imported.Hash.Hex
                && d.Repository == RepositoryType.Inbox
                && d.Extension == "jpg"
                && d.ContentType == "image/jpeg"
                && d.MediaUrl == $"/media/{imported.Hash.Hex}");
            details.Tags.Should().ContainSingle(tag =>
                tag.Namespace == "series" && tag.Subtag == "octans smoke test");
            details.Notes.Should().BeEmpty();
        }

        using (new AssertionScope("The media endpoint serves the imported bytes"))
        {
            mediaResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            mediaResponse.Content.Headers.ContentType?.MediaType.Should().Be("image/jpeg");
            mediaBytes.Should().Equal(TestingConstants.MinimalJpeg);
        }
    }

    [Fact]
    public async Task UserCan_MoveImportedMediaThroughInboxArchiveAndTrash_AndSearchReflectsLifecycle()
    {
        await using var factory = new OctansApiFactory(output);
        var client = factory.CreateClient();
        var imported = await ImportLocalImage(
            factory,
            client,
            "repository-lifecycle.jpg",
            TestingConstants.MinimalJpeg,
            []);

        var inboxResults = await Query(client, InboxQuery);
        var defaultResultsBeforeTrash = await Query(client, []);

        await TransitionRepository(client, factory, imported.Hash.Hex, RepositoryDestination.Archive);

        var inboxResultsAfterArchive = await Query(client, InboxQuery);
        var archiveResults = await Query(client, ["system:archive"]);
        var archivedDetails = await GetMediaDetails(client, imported.Hash.Hex);

        await TransitionRepository(client, factory, imported.Hash.Hex, RepositoryDestination.Trash);

        var defaultResultsAfterTrash = await Query(client, []);
        var trashResults = await Query(client, ["system:trash"]);
        var trashedDetails = await GetMediaDetails(client, imported.Hash.Hex);

        using (new AssertionScope("The imported media starts in the visible library"))
        {
            imported.ProcessedJob.Should().BeTrue("the lifecycle starts from a real completed import");
            inboxResults.Response.StatusCode.Should().Be(HttpStatusCode.OK);
            inboxResults.Items.Should().ContainSingle(item => item.Hash.SequenceEqual(imported.Hash.Bytes));
            defaultResultsBeforeTrash.Response.StatusCode.Should().Be(HttpStatusCode.OK);
            defaultResultsBeforeTrash.Items.Should().ContainSingle(item => item.Hash.SequenceEqual(imported.Hash.Bytes));
        }

        using (new AssertionScope("The archived media leaves inbox and appears in archive"))
        {
            inboxResultsAfterArchive.Response.StatusCode.Should().Be(HttpStatusCode.OK);
            inboxResultsAfterArchive.Items.Should().NotContain(item => item.Hash.SequenceEqual(imported.Hash.Bytes));
            archiveResults.Response.StatusCode.Should().Be(HttpStatusCode.OK);
            archiveResults.Items.Should().ContainSingle(item => item.Hash.SequenceEqual(imported.Hash.Bytes));
            archivedDetails.Repository.Should().Be(RepositoryType.Archive);
        }

        using (new AssertionScope("The trashed media leaves default search and appears in trash"))
        {
            defaultResultsAfterTrash.Response.StatusCode.Should().Be(HttpStatusCode.OK);
            defaultResultsAfterTrash.Items.Should().NotContain(item => item.Hash.SequenceEqual(imported.Hash.Bytes));
            trashResults.Response.StatusCode.Should().Be(HttpStatusCode.OK);
            trashResults.Items.Should().ContainSingle(item => item.Hash.SequenceEqual(imported.Hash.Bytes));
            trashedDetails.Repository.Should().Be(RepositoryType.Trash);
        }
    }

    [Fact]
    public async Task UserCan_ImportMediaWithTags_EditTags_AndSearchSeesCurrentTagState()
    {
        await using var factory = new OctansApiFactory(output);
        var client = factory.CreateClient();
        var initialTag = new TagModel("series", "metroid");
        var addedTag = new TagModel("character", "samus");
        var imported = await ImportLocalImage(
            factory,
            client,
            "tag-edit-flow.jpg",
            TestingConstants.MinimalJpeg,
            [initialTag]);

        var details = await GetMediaDetails(client, imported.Hash.Hex);

        var tagUpdateResponse = await UpdateTags(
            client,
            details.Id,
            [addedTag],
            [initialTag]);

        var updatedDetails = await GetMediaDetails(client, imported.Hash.Hex);
        var addedTagResults = await Query(client, [$"{addedTag.Namespace}:{addedTag.Subtag}"]);
        var removedTagResults = await Query(client, [$"{initialTag.Namespace}:{initialTag.Subtag}"]);

        using (new AssertionScope("The imported media exposes its initial tags through details"))
        {
            imported.ProcessedJob.Should().BeTrue("tag editing starts from a real completed import");
            details.Tags.Should().ContainSingle(tag =>
                tag.Namespace == initialTag.Namespace && tag.Subtag == initialTag.Subtag);
        }

        using (new AssertionScope("The tag update endpoint applies additions and removals"))
        {
            tagUpdateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            updatedDetails.Tags.Should().ContainSingle(tag =>
                tag.Namespace == addedTag.Namespace && tag.Subtag == addedTag.Subtag);
            updatedDetails.Tags.Should().NotContain(tag =>
                tag.Namespace == initialTag.Namespace && tag.Subtag == initialTag.Subtag);
        }

        using (new AssertionScope("Search uses the current tag state"))
        {
            addedTagResults.Response.StatusCode.Should().Be(HttpStatusCode.OK);
            addedTagResults.Items.Should().ContainSingle(item => item.Hash.SequenceEqual(imported.Hash.Bytes));
            removedTagResults.Response.StatusCode.Should().Be(HttpStatusCode.OK);
            removedTagResults.Items.Should().NotContain(item => item.Hash.SequenceEqual(imported.Hash.Bytes));
        }
    }

    [Fact]
    public async Task UserCan_AddEditViewAndDeleteNotesAttachedToImportedMedia()
    {
        await using var factory = new OctansApiFactory(output);
        var client = factory.CreateClient();
        var imported = await ImportLocalImage(
            factory,
            client,
            "notes-round-trip.jpg",
            TestingConstants.MinimalJpeg,
            []);

        var createResponse = await client.PostAsJsonAsync(
            new Uri($"/media/{imported.Hash.Hex}/notes", UriKind.Relative),
            new NoteCreateRequest("Initial field note"),
            JsonOptions);
        var createdNote = await createResponse.Content.ReadFromJsonAsync<NoteDto>(JsonOptions);
        var detailsAfterCreate = await GetMediaDetails(client, imported.Hash.Hex);

        var updateResponse = await client.PutAsJsonAsync(
            new Uri($"/notes/{createdNote!.Id}", UriKind.Relative),
            new NoteUpdateRequest("Updated field note"),
            JsonOptions);
        var detailsAfterUpdate = await GetMediaDetails(client, imported.Hash.Hex);

        var deleteResponse = await client.DeleteAsync(new Uri($"/notes/{createdNote.Id}", UriKind.Relative));
        var detailsAfterDelete = await GetMediaDetails(client, imported.Hash.Hex);

        using (new AssertionScope("The test starts from real imported media"))
        {
            imported.ProcessedJob.Should().BeTrue("notes attach to media that exists through the normal import pipeline");
            detailsAfterCreate.Hash.Should().Be(imported.Hash.Hex);
            detailsAfterCreate.MediaUrl.Should().Be($"/media/{imported.Hash.Hex}");
        }

        using (new AssertionScope("Adding a note makes it visible on media details"))
        {
            createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
            createResponse.Headers.Location?.OriginalString.Should().Be($"/notes/{createdNote.Id}");
            createdNote.Content.Should().Be("Initial field note");
            detailsAfterCreate.Notes.Should().ContainSingle(note =>
                note.Id == createdNote.Id && note.Content == "Initial field note");
        }

        using (new AssertionScope("Updating the note changes the user-visible media details"))
        {
            updateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
            detailsAfterUpdate.Notes.Should().ContainSingle(note =>
                note.Id == createdNote.Id && note.Content == "Updated field note");
        }

        using (new AssertionScope("Deleting the note removes it from media details"))
        {
            deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
            detailsAfterDelete.Notes.Should().NotContain(note => note.Id == createdNote.Id);
            detailsAfterDelete.Notes.Should().BeEmpty();
        }
    }

    private static async Task<ImportedImage> ImportLocalImage(
        OctansApiFactory factory,
        HttpClient client,
        string fileName,
        byte[] bytes,
        ICollection<TagModel> tags)
    {
        var imageStorage = factory.Services.GetRequiredService<ImageStorage>();
        imageStorage.EnsureStorage();

        var source = factory.FileSystem.Path.Join(factory.AppRoot, "imports", fileName);
        factory.FileSystem.AddFile(source, new(bytes));

        var hash = ContentHash.FromContent(bytes);
        var request = new ImportJobCreateRequest
        {
            ImportType = ImportType.File,
            Sources = [source],
            TagsBySource = new Dictionary<string, ICollection<TagModel>>
            {
                [source] = tags
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

        return new(source, hash, createResponse, created!, processedJob);
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

    private static async Task<MediaDetailsDto> GetMediaDetails(HttpClient client, string hash)
    {
        var response = await client.GetAsync(new Uri($"/media/{hash}/details", UriKind.Relative));
        var details = await response.Content.ReadFromJsonAsync<MediaDetailsDto>(JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return details!;
    }

    private static Task<HttpResponseMessage> UpdateTags(
        HttpClient client,
        int hashId,
        IEnumerable<TagModel> tagsToAdd,
        IEnumerable<TagModel> tagsToRemove)
    {
        var request = new UpdateTagsRequest(hashId, tagsToAdd, tagsToRemove);

        return client.PostAsJsonAsync(new Uri("/tags", UriKind.Relative), request, JsonOptions);
    }

    private static async Task TransitionRepository(
        HttpClient client,
        OctansApiFactory factory,
        string hash,
        RepositoryDestination destination)
    {
        var response = await client.PostAsJsonAsync(
            new Uri("/repository/transitions", UriKind.Relative),
            new RepositoryTransitionRequest([hash], destination),
            JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var reader = factory.Services.GetRequiredService<ChannelReader<RepositoryChangeRequest>>();
        var request = await reader.ReadAsync();
        var processor = factory.Services.GetRequiredService<RepositoryChangeProcessor>();

        await processor.ProcessBatch([request]);
    }

    private sealed record ImportedImage(
        string Source,
        ContentHash Hash,
        HttpResponseMessage CreateResponse,
        ImportJobCreatedDto Created,
        bool ProcessedJob);

    private sealed record QueryResult(HttpResponseMessage Response, IReadOnlyList<HashItem> Items);
}
