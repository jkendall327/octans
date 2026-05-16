using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Octans.Core;
using Octans.Core.Duplicates;
using Octans.Core.Filesystem;
using Octans.Data.Models;
using Octans.Data.Models.Duplicates;
using Octans.Tests.Helpers;
using Xunit.Abstractions;

namespace Octans.Tests.Duplicates;

public class DuplicateServiceTests : IAsyncLifetime, IClassFixture<DatabaseFixture>
{
    private readonly OctansTestHost _host;
    private readonly DuplicateService _sut;
    private readonly ServerDbContext _dbContext;
    private readonly IPerceptualHashProvider _hashProvider;

    public DuplicateServiceTests(ITestOutputHelper output, DatabaseFixture db)
    {
        _hashProvider = Substitute.For<IPerceptualHashProvider>();

        _host = OctansTestHost.Create(
            output,
            db,
            services => services.ReplaceExistingRegistrationsWith(_hashProvider),
            dbLifetime: ServiceLifetime.Scoped);

        _dbContext = _host.GetRequiredService<ServerDbContext>();
        _sut = _host.GetRequiredService<DuplicateService>();
    }

    public async Task InitializeAsync()
    {
        await _host.ResetDatabaseAsync();
        _host.EnsureImageStorage();
    }

    public Task DisposeAsync() => _host.DisposeAsync().AsTask();

    [Fact]
    public async Task CalculateMissingHashes_ShouldCalculateAndSaveHash()
    {
        // Arrange
        var bytes = new byte[] { 1, 2, 3 };
        var metadata = new ImageMetadata("jpg", "image/jpeg");
        await _host.AddStoredImageAsync(bytes, metadata, _dbContext);

        _hashProvider.GetHash(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(12345ul);

        // Act
        var count = await _sut.CalculateMissingHashes();

        // Assert
        count.Should().Be(1);
        var updated = await _dbContext.Hashes.FirstAsync();
        updated.PerceptualHash.Should().Be(12345ul);
    }

    [Fact]
    public async Task FindDuplicates_ShouldIdentifySimilarItems()
    {
        // Arrange
        var item1 = new HashItem { Hash = [1], PerceptualHash = 100 };
        var item2 = new HashItem { Hash = [2], PerceptualHash = 100 }; // Identical
        _dbContext.Hashes.AddRange(item1, item2);
        await _dbContext.SaveChangesAsync();

        // Act
        var count = await _sut.FindDuplicates();

        // Assert
        count.Should().Be(1);
        var candidate = await _dbContext.DuplicateCandidates.FirstOrDefaultAsync();
        candidate.Should().NotBeNull();
        candidate!.Distance.Should().Be(100.0);
        candidate.HashId1.Should().Be(item1.Id);
        candidate.HashId2.Should().Be(item2.Id);
    }

    [Fact]
    public async Task FindDuplicates_ShouldOnlyCreateCandidatesWithinSimilarityThreshold()
    {
        // Arrange
        var item1 = new HashItem { Hash = [1], PerceptualHash = 0 };
        var item2 = new HashItem { Hash = [2], PerceptualHash = 0b111 };
        var item3 = new HashItem
        {
            Hash = [3],
            PerceptualHash = (1UL << 60) | (1UL << 61) | (1UL << 62) | (1UL << 63)
        };
        _dbContext.Hashes.AddRange(item1, item2, item3);
        await _dbContext.SaveChangesAsync();

        // Act
        var count = await _sut.FindDuplicates();

        // Assert
        count.Should().Be(1);
        var candidate = await _dbContext.DuplicateCandidates.SingleAsync();
        candidate.HashId1.Should().Be(item1.Id);
        candidate.HashId2.Should().Be(item2.Id);
        candidate.Distance.Should().BeGreaterThanOrEqualTo(95);
    }

    [Fact]
    public async Task FindDuplicates_ShouldSkipExistingCandidates()
    {
        // Arrange
        var item1 = new HashItem { Hash = [1], PerceptualHash = 100 };
        var item2 = new HashItem { Hash = [2], PerceptualHash = 100 };
        _dbContext.Hashes.AddRange(item1, item2);

        var candidate = new DuplicateCandidate
        {
            Hash1 = item1,
            Hash2 = item2,
            Distance = 100
        };
        _dbContext.DuplicateCandidates.Add(candidate);
        await _dbContext.SaveChangesAsync();

        // Act
        var count = await _sut.FindDuplicates();

        // Assert
        count.Should().Be(0);
        _dbContext.DuplicateCandidates.Count().Should().Be(1);
    }

    [Fact]
    public async Task Resolve_ShouldCreateDecision_AndRemoveCandidate()
    {
        // Arrange
        var item1 = new HashItem { Hash = [1], PerceptualHash = 100 };
        var item2 = new HashItem { Hash = [2], PerceptualHash = 100 };
        _dbContext.Hashes.AddRange(item1, item2);

        var candidate = new DuplicateCandidate
        {
            Hash1 = item1,
            Hash2 = item2,
            Distance = 100
        };
        _dbContext.DuplicateCandidates.Add(candidate);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.Resolve(candidate.Id, DuplicateResolution.KeepBoth, null);

        // Assert
        _dbContext.DuplicateCandidates.Should().BeEmpty();
        var decision = await _dbContext.DuplicateDecisions.FirstAsync();
        decision.Resolution.Should().Be(DuplicateResolution.KeepBoth);
        decision.HashId1.Should().Be(item1.Id);
        decision.HashId2.Should().Be(item2.Id);
    }

    [Fact]
    public async Task FindDuplicates_ShouldRespectDecisions()
    {
        // Arrange
        var item1 = new HashItem { Hash = [1], PerceptualHash = 100 };
        var item2 = new HashItem { Hash = [2], PerceptualHash = 100 };
        _dbContext.Hashes.AddRange(item1, item2);

        var decision = new DuplicateDecision
        {
            Hash1 = item1,
            Hash2 = item2,
            Resolution = DuplicateResolution.KeepBoth
        };
        _dbContext.DuplicateDecisions.Add(decision);
        await _dbContext.SaveChangesAsync();

        // Act
        var count = await _sut.FindDuplicates();

        // Assert
        count.Should().Be(0);
        _dbContext.DuplicateCandidates.Should().BeEmpty();
    }

    [Fact]
    public async Task Resolve_WithKeepOne_ShouldDeleteTheOther()
    {
        // Arrange
        var item1 = new HashItem { Hash = [1], PerceptualHash = 100 };
        var item2 = new HashItem { Hash = [2], PerceptualHash = 100 };
        var item3 = new HashItem { Hash = [3], PerceptualHash = 100 };
        _dbContext.Hashes.AddRange(item1, item2, item3);

        var candidate = new DuplicateCandidate
        {
            Hash1 = item1,
            Hash2 = item2,
            Distance = 100
        };
        var otherCandidateWithDeletedHash = new DuplicateCandidate
        {
            Hash1 = item2,
            Hash2 = item3,
            Distance = 100
        };
        _dbContext.DuplicateCandidates.AddRange(candidate, otherCandidateWithDeletedHash);
        await _dbContext.SaveChangesAsync();

        // Simulate file existence for deletion
        var imageStorage = _host.GetRequiredService<ImageStorage>();
        var hash2 = ContentHash.FromHashBytes(item2.Hash);
        var metadata = new ImageMetadata("jpg", "image/jpeg");
        item2.Extension = metadata.Extension;
        item2.ContentType = metadata.ContentType;
        await _dbContext.SaveChangesAsync();
        var dest2 = imageStorage.GetOriginalDestination(hash2, metadata);
        _host.FileSystem.AddFile(dest2, new MockFileData("content"));

        // Act
        // Keep item1, so item2 should be deleted
        await _sut.Resolve(candidate.Id, DuplicateResolution.Distinct, item1.Id);

        // Assert
        _dbContext.DuplicateCandidates.Should().BeEmpty();
        _dbContext.DuplicateDecisions.Should().BeEmpty();

        var deletedItem2 = await _dbContext.Hashes.FindAsync(item2.Id);
        deletedItem2!.DeletedAt.Should().NotBeNull();

        _host.FileSystem.FileExists(dest2).Should().BeFalse();
    }

    [Fact]
    public async Task Resolve_WithUnknownKeepHashId_ShouldThrowAndLeaveCandidateAlone()
    {
        // Arrange
        var item1 = new HashItem { Hash = [1], PerceptualHash = 100 };
        var item2 = new HashItem { Hash = [2], PerceptualHash = 100 };
        var unrelatedItem = new HashItem { Hash = [3], PerceptualHash = 100 };
        _dbContext.Hashes.AddRange(item1, item2, unrelatedItem);

        var candidate = new DuplicateCandidate
        {
            Hash1 = item1,
            Hash2 = item2,
            Distance = 100
        };
        _dbContext.DuplicateCandidates.Add(candidate);
        await _dbContext.SaveChangesAsync();

        // Act
        var act = async () => await _sut.Resolve(candidate.Id, DuplicateResolution.Distinct, unrelatedItem.Id);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage($"*Hash {unrelatedItem.Id} is not part of duplicate candidate {candidate.Id}*");
        _dbContext.DuplicateCandidates.Should().ContainSingle();
        _dbContext.Hashes.Should().OnlyContain(hash => hash.DeletedAt == null);
        _dbContext.DuplicateDecisions.Should().BeEmpty();
    }
}
