using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Octans.Client;
using Octans.Core;
using Octans.Core.Duplicates;
using Octans.Data.Models;
using Octans.Data.Models.Duplicates;
using Octans.Tests.Helpers;
using Xunit.Abstractions;

namespace Octans.Tests.UserFlows;

public sealed class DuplicateReviewFlowTests(ITestOutputHelper output)
{
    [Fact]
    public async Task UserCan_DiscoverReviewAndSafelyResolveDuplicateCandidates()
    {
        var hashProvider = new ControlledPerceptualHashProvider();
        await using var factory = new OctansApiFactory(
            output,
            services => services.ReplaceExistingRegistrationsWith<IPerceptualHashProvider>(hashProvider));
        var client = factory.CreateClient();

        var library = await SeedDuplicateReviewLibrary(factory);
        hashProvider.SetHash(library["calculated-duplicate-a"].Hash, library["seeded-duplicate-a"].PerceptualHash!.Value);

        var scanResponse = await client.PostAsync(new Uri("/duplicates/scan", UriKind.Relative), null);
        var scanResult = await scanResponse.Content.ReadFromJsonAsync<DuplicateScanResultDto>(OctansApiFactory.JsonOptions);
        var candidates = await GetDuplicateCandidates(client);

        var notDuplicateCandidate = candidates.Single(candidate =>
            candidate.Contains(library["calculated-duplicate-a"], library["seeded-duplicate-a"]));
        var deleteCandidate = candidates.Single(candidate =>
            candidate.Contains(library["delete-review-keep"], library["delete-review-remove"]));

        var candidateMediaResponses = await ResolveCandidateMediaUrls(client, candidates);

        var notDuplicateResponse = await ResolveCandidate(
            client,
            notDuplicateCandidate.Id,
            DuplicateResolution.Distinct,
            keepHashId: null);
        var rescanAfterNotDuplicateResponse = await client.PostAsync(new Uri("/duplicates/scan", UriKind.Relative), null);
        var rescanAfterNotDuplicate = await rescanAfterNotDuplicateResponse
            .Content
            .ReadFromJsonAsync<DuplicateScanResultDto>(OctansApiFactory.JsonOptions);
        var candidatesAfterNotDuplicate = await GetDuplicateCandidates(client);

        var deleteResponse = await ResolveCandidate(
            client,
            deleteCandidate.Id,
            DuplicateResolution.Distinct,
            library["delete-review-keep"].Id);
        var candidatesAfterDelete = await GetDuplicateCandidates(client);
        var normalSearchAfterDelete = await OctansApiFactory.QueryAsync(client, []);
        var keptMediaResponse = await client.GetAsync(
            new Uri(library["delete-review-keep"].MediaUrl, UriKind.Relative));
        var removedMediaResponse = await client.GetAsync(
            new Uri(library["delete-review-remove"].MediaUrl, UriKind.Relative));
        var durableDecisions = await GetDurableDecisions(factory);

        using (new AssertionScope("Scanning calculates missing hashes and creates only likely duplicate candidates"))
        {
            scanResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            scanResult.Should().Be(new DuplicateScanResultDto(1, 2));
            candidates.Should().HaveCount(2);
            candidates.Should().Contain(candidate =>
                candidate.Contains(library["calculated-duplicate-a"], library["seeded-duplicate-a"]));
            candidates.Should().Contain(candidate =>
                candidate.Contains(library["delete-review-keep"], library["delete-review-remove"]));
            candidates.Should().NotContain(candidate =>
                candidate.Contains(library["non-match"], library["seeded-duplicate-a"])
                || candidate.Contains(library["non-match"], library["delete-review-keep"]));
        }

        using (new AssertionScope("The duplicate review data is stable and points at real media"))
        {
            foreach (var candidate in candidates)
            {
                candidate.Id.Should().BePositive();
                candidate.HashId1.Should().BePositive();
                candidate.HashId2.Should().BePositive();
                candidate.Hash1.Should().HaveLength(64);
                candidate.Hash2.Should().HaveLength(64);
                candidate.MediaUrl1.Should().Be($"/media/{candidate.Hash1}");
                candidate.MediaUrl2.Should().Be($"/media/{candidate.Hash2}");
                candidate.Distance.Should().BeGreaterThanOrEqualTo(95.0);
            }

            candidateMediaResponses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.OK);
            candidateMediaResponses
                .Select(response => response.Content.Headers.ContentType?.MediaType)
                .Should()
                .OnlyContain(mediaType => mediaType == "image/jpeg");
        }

        using (new AssertionScope("Resolving a pair as not duplicates is durable across later scans"))
        {
            notDuplicateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
            rescanAfterNotDuplicateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            rescanAfterNotDuplicate.Should().Be(new DuplicateScanResultDto(0, 0));
            candidatesAfterNotDuplicate.Should().ContainSingle();
            candidatesAfterNotDuplicate.Should().NotContain(candidate =>
                candidate.Contains(library["calculated-duplicate-a"], library["seeded-duplicate-a"]));
            durableDecisions.Should().Contain(decision =>
                decision.Resolution == DuplicateResolution.Distinct
                && DecisionContains(decision, library["calculated-duplicate-a"], library["seeded-duplicate-a"]));
        }

        using (new AssertionScope("Resolving by keeping one side removes the other without harming the keeper"))
        {
            deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
            candidatesAfterDelete.Should().BeEmpty();
            normalSearchAfterDelete.Response.StatusCode.Should().Be(HttpStatusCode.OK);
            normalSearchAfterDelete.Hashes.Should().Contain(library["delete-review-keep"].Hash.Hex);
            normalSearchAfterDelete.Hashes.Should().NotContain(library["delete-review-remove"].Hash.Hex);
            keptMediaResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            removedMediaResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        using (new AssertionScope("Duplicate resolution keeps durable review history"))
        {
            durableDecisions.Should().Contain(decision =>
                DecisionContains(decision, library["delete-review-keep"], library["delete-review-remove"]));
        }
    }

    private static async Task<IReadOnlyDictionary<string, SeededMedia>> SeedDuplicateReviewLibrary(
        OctansApiFactory factory)
    {
        await using var scope = factory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();

        var media = new[]
        {
            CreateMedia("calculated-duplicate-a", null),
            CreateMedia("seeded-duplicate-a", 0UL),
            CreateMedia("delete-review-keep", ulong.MaxValue),
            CreateMedia("delete-review-remove", ulong.MaxValue ^ 1UL),
            CreateMedia("non-match", 0x00FF_00FF_00FF_00FFUL)
        };

        foreach (var item in media)
        {
            await factory.AddStoredImageAsync(
                db,
                item.Bytes,
                perceptualHash: item.PerceptualHash,
                metadata: new("jpg", "image/jpeg"));
        }

        await db.SaveChangesAsync();

        foreach (var item in media)
        {
            item.Id = db.Hashes
                .AsEnumerable()
                .Single(hash => hash.Hash.SequenceEqual(item.Hash.Bytes))
                .Id;
        }

        return media.ToDictionary(item => item.Name);
    }

    private static SeededMedia CreateMedia(string name, ulong? perceptualHash)
    {
        var bytes = TestingConstants.MinimalJpeg
            .Concat("\n"u8.ToArray())
            .Concat(JsonSerializer.SerializeToUtf8Bytes(name))
            .ToArray();
        var hash = ContentHash.FromContent(bytes);

        return new(name, hash, bytes, perceptualHash);
    }

    private static async Task<IReadOnlyList<DuplicateCandidateResult>> GetDuplicateCandidates(HttpClient client)
    {
        var response = await client.GetAsync(new Uri("/duplicates/candidates", UriKind.Relative));
        var candidates = await response.Content.ReadFromJsonAsync<List<DuplicateCandidateDto>>(OctansApiFactory.JsonOptions) ?? [];

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return candidates
            .Select(candidate => new DuplicateCandidateResult(candidate))
            .ToList();
    }

    private static async Task<HttpResponseMessage> ResolveCandidate(
        HttpClient client,
        int candidateId,
        DuplicateResolution resolution,
        int? keepHashId)
    {
        return await client.PostAsJsonAsync(
            new Uri($"/duplicates/candidates/{candidateId}/resolution", UriKind.Relative),
            new DuplicateResolutionRequest(resolution, keepHashId),
            OctansApiFactory.JsonOptions);
    }

    private static async Task<IReadOnlyList<HttpResponseMessage>> ResolveCandidateMediaUrls(
        HttpClient client,
        IReadOnlyList<DuplicateCandidateResult> candidates)
    {
        var responses = new List<HttpResponseMessage>();

        foreach (var candidate in candidates)
        {
            responses.Add(await client.GetAsync(new Uri(candidate.MediaUrl1, UriKind.Relative)));
            responses.Add(await client.GetAsync(new Uri(candidate.MediaUrl2, UriKind.Relative)));
        }

        return responses;
    }

    private static async Task<IReadOnlyList<DuplicateDecision>> GetDurableDecisions(OctansApiFactory factory)
    {
        await using var scope = factory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();

        return await db.DuplicateDecisions
            .AsNoTracking()
            .OrderBy(decision => decision.Id)
            .ToListAsync();
    }

    private static bool DecisionContains(DuplicateDecision decision, SeededMedia first, SeededMedia second)
    {
        return decision.HashId1 == first.Id && decision.HashId2 == second.Id
            || decision.HashId1 == second.Id && decision.HashId2 == first.Id;
    }

    private sealed class ControlledPerceptualHashProvider : IPerceptualHashProvider
    {
        private readonly Dictionary<ContentHash, ulong> _hashes = [];

        public void SetHash(ContentHash hash, ulong perceptualHash)
        {
            _hashes[hash] = perceptualHash;
        }

        public async Task<ulong> GetHash(Stream imageStream, CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await imageStream.CopyToAsync(buffer, cancellationToken);
            var hash = ContentHash.FromContent(buffer.ToArray());

            return _hashes[hash];
        }
    }

    private sealed record DuplicateCandidateResult(
        int Id,
        int HashId1,
        string Hash1,
        string MediaUrl1,
        int HashId2,
        string Hash2,
        string MediaUrl2,
        double Distance)
    {
        public DuplicateCandidateResult(DuplicateCandidateDto dto)
            : this(dto.Id, dto.HashId1, dto.Hash1, dto.MediaUrl1, dto.HashId2, dto.Hash2, dto.MediaUrl2, dto.Distance)
        {
        }

        public bool Contains(SeededMedia first, SeededMedia second)
        {
            var firstHash = first.Hash.Hex;
            var secondHash = second.Hash.Hex;

            return Hash1 == firstHash && Hash2 == secondHash
                || Hash1 == secondHash && Hash2 == firstHash;
        }
    }

    private sealed class SeededMedia(
        string name,
        ContentHash hash,
        byte[] bytes,
        ulong? perceptualHash)
    {
        public int Id { get; set; }
        public string Name { get; } = name;
        public ContentHash Hash { get; } = hash;
        public byte[] Bytes { get; } = bytes;
        public ulong? PerceptualHash { get; } = perceptualHash;
        public string MediaUrl => $"/media/{Hash.Hex}";
    }
}
