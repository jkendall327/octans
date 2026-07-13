using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Octans.Client;
using Octans.Core;
using Octans.Core.Querying;
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

    [Fact]
    public async Task NegativeOnlyQuery_SubtractsMatchesFromNormalLibraryScope()
    {
        await AssertQueryReturns(
            ["-character:samus"],
            ["ridley-metroid-archive", "mario-kart-inbox"]);
    }

    [Fact]
    public async Task PositiveAndNegativePredicates_UseSetSubtraction()
    {
        await AssertQueryReturns(
            ["series:metroid", "-character:ridley"],
            ["samus-metroid-inbox"]);
    }

    [Fact]
    public async Task ExplicitOrGroup_MatchesAnyAlternative()
    {
        await AssertQueryReturns(
            ["or:character:samus OR character:mario"],
            ["samus-metroid-inbox", "samus-smash-archive", "mario-kart-inbox"]);
    }

    [Fact]
    public async Task NestedOrGroup_PreservesItsTreeSemantics()
    {
        await AssertQueryReturns(
            ["or:character:ridley OR (or:character:mario OR series:smash)"],
            ["ridley-metroid-archive", "samus-smash-archive", "mario-kart-inbox"]);
    }

    [Fact]
    public async Task NegativeAlternativeInsideOrGroup_IsARealNotPredicate()
    {
        await AssertQueryReturns(
            ["or:character:ridley OR -series:metroid"],
            ["ridley-metroid-archive", "samus-smash-archive", "mario-kart-inbox"]);
    }

    [Fact]
    public async Task NonTrashRepositoryAlternative_DoesNotOpenTrashScopeForOtherAlternatives()
    {
        await AssertQueryReturns(
            ["or:system:inbox OR character:bowser"],
            ["samus-metroid-inbox", "mario-kart-inbox"]);
    }

    [Fact]
    public async Task TrashAlternative_ExplicitlyOpensTrashScope()
    {
        await AssertQueryReturns(
            ["or:system:trash OR character:samus"],
            ["samus-metroid-inbox", "samus-smash-archive", "bowser-trash"]);
    }

    [Theory]
    [InlineData("character:sam*", "samus-metroid-inbox", "samus-smash-archive")]
    [InlineData("character:*", "samus-metroid-inbox", "ridley-metroid-archive", "samus-smash-archive", "mario-kart-inbox")]
    [InlineData("*:met*", "samus-metroid-inbox", "ridley-metroid-archive")]
    [InlineData("*:*kart*", "mario-kart-inbox")]
    public async Task Wildcards_MatchNamespaceAndSubtagPatterns(string query, params string[] expected)
    {
        await AssertQueryReturns([query], expected);
    }

    [Fact]
    public async Task WildcardNegation_SubtractsEveryMatchingTag()
    {
        await AssertQueryReturns(
            ["-series:met*"],
            ["samus-smash-archive", "mario-kart-inbox"]);
    }

    [Fact]
    public async Task SystemEverything_DoesNotDiscardOtherPredicates()
    {
        await AssertQueryReturns(
            ["system:everything", "character:samus"],
            ["samus-metroid-inbox", "samus-smash-archive"]);
    }

    [Fact]
    public async Task TagMatching_IsCaseInsensitive()
    {
        await AssertQueryReturns(
            ["CHARACTER:SAMUS"],
            ["samus-metroid-inbox", "samus-smash-archive"]);
    }

    [Fact]
    public async Task ParentTags_MatchMediaMappedToTheirDescendantTags()
    {
        await AssertQueryReturns(
            ["franchise:nintendo"],
            ["samus-metroid-inbox", "ridley-metroid-archive"]);
    }

    [Fact]
    public async Task NegatedParentTags_ExcludeMediaMappedToTheirDescendantTags()
    {
        await AssertQueryReturns(
            ["-franchise:nintendo"],
            ["samus-smash-archive", "mario-kart-inbox"]);
    }

    [Fact]
    public async Task PagedQuery_ReturnsStableSliceAndTotal()
    {
        await using var factory = new OctansApiFactory(output);
        var client = factory.CreateClient();
        await SeedLibrary(factory);

        var response = await client.PostAsJsonAsync(
            "/api/files/query",
            new FileQueryRequest([], Offset: 1, Limit: 2));
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        var page = await response.Content.ReadFromJsonAsync<FileQueryPageDto>(jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        page.Should().NotBeNull();
        page!.Total.Should().Be(4);
        page.Offset.Should().Be(1);
        page.Limit.Should().Be(2);
        page.Items.Should().HaveCount(2);
        page.Items.Select(item => item.Id).Should().BeInAscendingOrder();
    }

    [Theory]
    [InlineData("system:nope", "unsupported_system_predicate")]
    [InlineData("or:character:samus", "or_requires_alternatives")]
    [InlineData("or:character:samus OR (character:mario", "unbalanced_parenthesis")]
    [InlineData("character:", "empty_subtag")]
    public async Task InvalidQuery_ReturnsStructuredBadRequest(string query, string expectedCode)
    {
        await using var factory = new OctansApiFactory(output);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/files/query",
            new FileQueryRequest([query]));
        var error = await response.Content.ReadFromJsonAsync<QueryErrorResponse>();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        error.Should().NotBeNull();
        error!.Errors.Should().ContainSingle();
        error.Errors[0].Code.Should().Be(expectedCode);
        error.Errors[0].PredicateIndex.Should().Be(0);
        error.Errors[0].Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task QuerySuggestions_IncludeSystemPredicatesAndTags()
    {
        await using var factory = new OctansApiFactory(output);
        var client = factory.CreateClient();
        await SeedLibrary(factory);

        var system = await client.GetFromJsonAsync<QueryLanguageSuggestionsDto>(
            "/api/query/suggestions?search=system:ar");
        var tags = await client.GetFromJsonAsync<QueryLanguageSuggestionsDto>(
            "/api/query/suggestions?search=sam");

        system!.Suggestions.Should().Contain(s => s.Value == "system:archive" && s.Kind == "system");
        tags!.Suggestions.Should().Contain(s => s.Value == "character:samus" && s.Kind == "tag");
    }

    [Fact]
    public async Task QuerySuggestions_PreserveTagNegation()
    {
        await using var factory = new OctansApiFactory(output);
        var client = factory.CreateClient();
        await SeedLibrary(factory);

        var result = await client.GetFromJsonAsync<QueryLanguageSuggestionsDto>(
            "/api/query/suggestions?search=-sam");

        result!.Suggestions.Should().Contain(s => s.Value == "-character:samus");
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

        var tags = new Dictionary<TagModel, Tag>();
        foreach (var item in media)
        {
            var stored = await factory.AddStoredImageAsync(
                db,
                item.Bytes,
                item.Repository,
                metadata: new("jpg", "image/jpeg"));

            foreach (var tagModel in item.Tags)
            {
                if (!tags.TryGetValue(tagModel, out var tag))
                {
                    tag = new Tag
                    {
                        Namespace = new Namespace { Value = tagModel.Namespace ?? string.Empty },
                        Subtag = new Subtag { Value = tagModel.Subtag }
                    };
                    tags[tagModel] = tag;
                }

                db.Mappings.Add(new Mapping
                {
                    Hash = stored.Entity,
                    Tag = tag
                });
            }
        }

        db.TagParents.Add(new TagParent
        {
            Child = tags[new("series", "metroid")],
            Parent = new Tag
            {
                Namespace = new Namespace { Value = "franchise" },
                Subtag = new Subtag { Value = "nintendo" }
            }
        });

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
