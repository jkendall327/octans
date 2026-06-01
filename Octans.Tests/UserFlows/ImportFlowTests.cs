using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
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
            OctansApiFactory.JsonOptions);
        var inboxResults = await OctansApiFactory.QueryAsync(client, InboxQuery);
        var detailsResponse = await client.GetAsync(
            new Uri($"/media/{imported.Hash.Hex}/details", UriKind.Relative));
        var details = await detailsResponse.Content.ReadFromJsonAsync<MediaDetailsDto>(OctansApiFactory.JsonOptions);
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

        var inboxResults = await OctansApiFactory.QueryAsync(client, InboxQuery);
        var defaultResultsBeforeTrash = await OctansApiFactory.QueryAsync(client, []);

        await TransitionRepository(client, factory, imported.Hash.Hex, RepositoryDestination.Archive);

        var inboxResultsAfterArchive = await OctansApiFactory.QueryAsync(client, InboxQuery);
        var archiveResults = await OctansApiFactory.QueryAsync(client, ["system:archive"]);
        var archivedDetails = await GetMediaDetails(client, imported.Hash.Hex);

        await TransitionRepository(client, factory, imported.Hash.Hex, RepositoryDestination.Trash);

        var defaultResultsAfterTrash = await OctansApiFactory.QueryAsync(client, []);
        var trashResults = await OctansApiFactory.QueryAsync(client, ["system:trash"]);
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
        var addedTagResults = await OctansApiFactory.QueryAsync(client, [$"{addedTag.Namespace}:{addedTag.Subtag}"]);
        var removedTagResults = await OctansApiFactory.QueryAsync(client, [$"{initialTag.Namespace}:{initialTag.Subtag}"]);

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
            OctansApiFactory.JsonOptions);
        var createdNote = await createResponse.Content.ReadFromJsonAsync<NoteDto>(OctansApiFactory.JsonOptions);
        var detailsAfterCreate = await GetMediaDetails(client, imported.Hash.Hex);

        var updateResponse = await client.PutAsJsonAsync(
            new Uri($"/notes/{createdNote!.Id}", UriKind.Relative),
            new NoteUpdateRequest("Updated field note"),
            OctansApiFactory.JsonOptions);
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
            OctansApiFactory.JsonOptions);
        var created = await createResponse.Content.ReadFromJsonAsync<ImportJobCreatedDto>(OctansApiFactory.JsonOptions);

        var processedJob = await factory.ProcessQueuedImportJobAsync();

        return new(source, hash, createResponse, created!, processedJob);
    }

    private static async Task<MediaDetailsDto> GetMediaDetails(HttpClient client, string hash)
    {
        var response = await client.GetAsync(new Uri($"/media/{hash}/details", UriKind.Relative));
        var details = await response.Content.ReadFromJsonAsync<MediaDetailsDto>(OctansApiFactory.JsonOptions);

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

        return client.PostAsJsonAsync(new Uri("/tags", UriKind.Relative), request, OctansApiFactory.JsonOptions);
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
            OctansApiFactory.JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        await factory.ProcessNextRepositoryChangeAsync();
    }

    private sealed record ImportedImage(
        string Source,
        ContentHash Hash,
        HttpResponseMessage CreateResponse,
        ImportJobCreatedDto Created,
        bool ProcessedJob);
}
