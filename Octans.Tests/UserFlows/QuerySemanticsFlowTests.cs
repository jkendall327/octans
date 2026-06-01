using System.Net;
using System.Text.Json;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Octans.Core;
using Octans.Core.Tags;
using Octans.Data.Models;
using Octans.Data.Models.Tagging;
using Octans.Tests.Helpers;
using Xunit.Abstractions;

namespace Octans.Tests.UserFlows;

public sealed class QuerySemanticsFlowTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ExactTags_ReturnEveryMatchingNonTrashFile()
    {
        await AssertQueryReturns(
            ["character:samus"],
            ["samus-metroid-inbox", "samus-smash-archive"]);
    }

    [Fact]
    public async Task MultiplePositiveTags_AreStrictAndFilters()
    {
        await AssertQueryReturns(
            ["character:samus", "series:metroid"],
            ["samus-metroid-inbox"]);
    }

    [Fact]
    public async Task EmptySearch_UsesNormalNonTrashLibraryScope()
    {
        await AssertQueryReturns(
            [],
            ["samus-metroid-inbox", "ridley-metroid-archive", "samus-smash-archive", "mario-kart-inbox"]);
    }

    [Fact]
    public async Task SystemEverything_UsesNormalNonTrashLibraryScope()
    {
        await AssertQueryReturns(
            ["system:everything"],
            ["samus-metroid-inbox", "ridley-metroid-archive", "samus-smash-archive", "mario-kart-inbox"]);
    }

    [Fact]
    public async Task SystemInbox_ReturnsInboxMediaOnly()
    {
        await AssertQueryReturns(
            ["system:inbox"],
            ["samus-metroid-inbox", "mario-kart-inbox"]);
    }

    [Fact]
    public async Task SystemArchive_ReturnsArchivedMediaOnly()
    {
        await AssertQueryReturns(
            ["system:archive"],
            ["ridley-metroid-archive", "samus-smash-archive"]);
    }

    [Fact]
    public async Task SystemTrash_ReturnsTrashedMediaOnly()
    {
        await AssertQueryReturns(
            ["system:trash"],
            ["bowser-trash"]);
    }

    [Fact]
    public async Task DefaultSearch_ExcludesTrashEvenWhenTrashedFileHasMatchingTag()
    {
        await AssertQueryReturns(
            ["series:mario"],
            []);
    }

    [Fact]
    public async Task ExplicitTrashSearch_CanBeCombinedWithOrdinaryTagPredicate()
    {
        await AssertQueryReturns(
            ["system:trash", "series:mario"],
            ["bowser-trash"]);
    }

    private async Task AssertQueryReturns(string[] query, string[] expectedNames)
    {
        await using var factory = new OctansApiFactory(output);
        var client = factory.CreateClient();

        var library = await SeedLibrary(factory);
        var queryResult = await OctansApiFactory.QueryAsync(client, query);
        var countResult = await OctansApiFactory.CountQueryAsync(client, query);
        var expectedHashes = expectedNames
            .Select(name => library[name].Hash.Hex)
            .ToArray();

        using var _ = new AssertionScope();

        queryResult.Response.StatusCode.Should().Be(HttpStatusCode.OK);
        countResult.Response.StatusCode.Should().Be(HttpStatusCode.OK);
        queryResult.Hashes.Should().BeEquivalentTo(expectedHashes);
        countResult.Count.Should().Be(expectedHashes.Length);
    }

    private static async Task<IReadOnlyDictionary<string, SeededMedia>> SeedLibrary(OctansApiFactory factory)
    {
        await using var scope = factory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();

        var media = new[]
        {
            CreateMedia(
                "samus-metroid-inbox",
                RepositoryType.Inbox,
                [new("character", "samus"), new("series", "metroid")]),
            CreateMedia(
                "ridley-metroid-archive",
                RepositoryType.Archive,
                [new("character", "ridley"), new("series", "metroid")]),
            CreateMedia(
                "samus-smash-archive",
                RepositoryType.Archive,
                [new("character", "samus"), new("series", "smash")]),
            CreateMedia(
                "mario-kart-inbox",
                RepositoryType.Inbox,
                [new("character", "mario"), new("series", "mario kart")]),
            CreateMedia(
                "bowser-trash",
                RepositoryType.Trash,
                [new("character", "bowser"), new("series", "mario")])
        };

        foreach (var item in media)
        {
            var stored = await factory.AddStoredImageAsync(
                db,
                item.Bytes,
                item.Repository,
                metadata: new("jpg", "image/jpeg"));

            foreach (var tagModel in item.Tags)
            {
                var tag = new Tag
                {
                    Namespace = new Namespace { Value = tagModel.Namespace ?? string.Empty },
                    Subtag = new Subtag { Value = tagModel.Subtag }
                };

                db.Mappings.Add(new Mapping
                {
                    Hash = stored.Entity,
                    Tag = tag
                });
            }
        }

        await db.SaveChangesAsync();

        return media.ToDictionary(item => item.Name);
    }

    private static SeededMedia CreateMedia(string name, RepositoryType repository, IReadOnlyList<TagModel> tags)
    {
        var bytes = TestingConstants.MinimalJpeg
            .Concat("\n"u8.ToArray())
            .Concat(JsonSerializer.SerializeToUtf8Bytes(name))
            .ToArray();
        var hash = ContentHash.FromContent(bytes);

        return new(name, hash, bytes, repository, tags);
    }
    

    private readonly record struct SeededMedia(
        string Name,
        ContentHash Hash,
        byte[] Bytes,
        RepositoryType Repository,
        IReadOnlyList<TagModel> Tags);

}
