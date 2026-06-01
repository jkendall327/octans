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
    public async Task UserCan_SearchWithDocumentedCoreQuerySemantics_ThroughTheFilesQueryApi()
    {
        await using var factory = new OctansApiFactory(output);
        var client = factory.CreateClient();

        var library = await SeedLibrary(factory);

        var expectations = new QueryExpectation[]
        {
            new(
                "Exact tags return every matching non-trash file",
                ["character:samus"],
                ["samus-metroid-inbox", "samus-smash-archive"]),
            new(
                "Multiple positive tag predicates are strict AND filters",
                ["character:samus", "series:metroid"],
                ["samus-metroid-inbox"]),
            new(
                "Empty search uses the normal non-trash library scope",
                [],
                ["samus-metroid-inbox", "ridley-metroid-archive", "samus-smash-archive", "mario-kart-inbox"]),
            new(
                "system:everything uses the same normal non-trash library scope",
                ["system:everything"],
                ["samus-metroid-inbox", "ridley-metroid-archive", "samus-smash-archive", "mario-kart-inbox"]),
            new(
                "system:inbox returns inbox media only",
                ["system:inbox"],
                ["samus-metroid-inbox", "mario-kart-inbox"]),
            new(
                "system:archive returns archived media only",
                ["system:archive"],
                ["ridley-metroid-archive", "samus-smash-archive"]),
            new(
                "system:trash returns trashed media only",
                ["system:trash"],
                ["bowser-trash"]),
            new(
                "Default search excludes trash even when a trashed file has the matching tag",
                ["series:mario"],
                []),
            new(
                "Explicit trash search can be combined with an ordinary tag predicate",
                ["system:trash", "series:mario"],
                ["bowser-trash"])
        };

        foreach (var expectation in expectations)
        {
            var queryResult = await OctansApiFactory.QueryAsync(client, expectation.Query);
            var countResult = await OctansApiFactory.CountQueryAsync(client, expectation.Query);
            var expectedHashes = expectation.ExpectedNames
                .Select(name => library[name].Hash.Hex)
                .ToArray();

            using var _ = new AssertionScope(expectation.Description);

            queryResult.Response.StatusCode.Should().Be(HttpStatusCode.OK);
            countResult.Response.StatusCode.Should().Be(HttpStatusCode.OK);
            queryResult.Hashes.Should().BeEquivalentTo(expectedHashes);
            countResult.Count.Should().Be(expectedHashes.Length);
        }
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

    private sealed record QueryExpectation(
        string Description,
        string[] Query,
        string[] ExpectedNames);

    private readonly record struct SeededMedia(
        string Name,
        ContentHash Hash,
        byte[] Bytes,
        RepositoryType Repository,
        IReadOnlyList<TagModel> Tags);

}
