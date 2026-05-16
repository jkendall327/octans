using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
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
    private readonly DatabaseFixture _databaseFixture;
    private readonly ServiceProvider _provider;
    private readonly DuplicateService _sut;
    private readonly ServerDbContext _dbContext;
    private readonly MockFileSystem _fileSystem;
    private readonly IPerceptualHashProvider _hashProvider;
    private readonly string _appRoot = "/app";

    public DuplicateServiceTests(ITestOutputHelper testOutputHelper, DatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture;
        var services = new ServiceCollection();

        services.AddLogging(s => s.AddProvider(new XUnitLoggerProvider(testOutputHelper)));

        databaseFixture.RegisterDbContext(services, ServiceLifetime.Scoped);
        
        services.AddScoped<DuplicateService>();
        services.AddScoped<FileDeleter>();

        _fileSystem = new MockFileSystem();
        services.AddSingleton<IFileSystem>(_fileSystem);
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(TestClock.UtcNow));
        services.Configure<GlobalSettings>(s => s.AppRoot = _appRoot);
        services.AddSingleton<ImageStorage>();

        _hashProvider = Substitute.For<IPerceptualHashProvider>();
        services.AddSingleton(_hashProvider);

        _provider = services.BuildServiceProvider();
        _dbContext = _provider.GetRequiredService<ServerDbContext>();
        _sut = _provider.GetRequiredService<DuplicateService>();

        var imageStorage = _provider.GetRequiredService<ImageStorage>();
        imageStorage.EnsureStorage();
    }

    public async Task InitializeAsync()
    {
        await DatabaseFixture.ResetAsync(_provider);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CalculateMissingHashes_ShouldCalculateAndSaveHash()
    {
        // Arrange
        var bytes = new byte[] { 1, 2, 3 };
        var hash = ContentHash.FromContent(bytes);
        var metadata = new ImageMetadata("jpg", "image/jpeg");

        var hashItem = new HashItem
        {
            Hash = hash.Bytes,
            Extension = metadata.Extension,
            ContentType = metadata.ContentType
        };
        _dbContext.Hashes.Add(hashItem);
        await _dbContext.SaveChangesAsync();

        var imageStorage = _provider.GetRequiredService<ImageStorage>();
        var destination = imageStorage.GetOriginalDestination(hash, metadata);
        _fileSystem.AddFile(destination, new MockFileData(bytes));

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
        var item1 = new HashItem { Hash = new byte[] { 1 }, PerceptualHash = 100 };
        var item2 = new HashItem { Hash = new byte[] { 2 }, PerceptualHash = 100 }; // Identical
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
    public async Task FindDuplicates_ShouldSkipExistingCandidates()
    {
        // Arrange
        var item1 = new HashItem { Hash = new byte[] { 1 }, PerceptualHash = 100 };
        var item2 = new HashItem { Hash = new byte[] { 2 }, PerceptualHash = 100 };
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
        var item1 = new HashItem { Hash = new byte[] { 1 }, PerceptualHash = 100 };
        var item2 = new HashItem { Hash = new byte[] { 2 }, PerceptualHash = 100 };
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
        var item1 = new HashItem { Hash = new byte[] { 1 }, PerceptualHash = 100 };
        var item2 = new HashItem { Hash = new byte[] { 2 }, PerceptualHash = 100 };
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
        var item1 = new HashItem { Hash = new byte[] { 1 }, PerceptualHash = 100 };
        var item2 = new HashItem { Hash = new byte[] { 2 }, PerceptualHash = 100 };
        _dbContext.Hashes.AddRange(item1, item2);

        var candidate = new DuplicateCandidate
        {
            Hash1 = item1,
            Hash2 = item2,
            Distance = 100
        };
        _dbContext.DuplicateCandidates.Add(candidate);
        await _dbContext.SaveChangesAsync();

        // Simulate file existence for deletion
        var imageStorage = _provider.GetRequiredService<ImageStorage>();
        var hash2 = ContentHash.FromHashBytes(item2.Hash);
        var metadata = new ImageMetadata("jpg", "image/jpeg");
        item2.Extension = metadata.Extension;
        item2.ContentType = metadata.ContentType;
        await _dbContext.SaveChangesAsync();
        var dest2 = imageStorage.GetOriginalDestination(hash2, metadata);
        _fileSystem.AddFile(dest2, new MockFileData("content"));

        // Act
        // Keep item1, so item2 should be deleted
        await _sut.Resolve(candidate.Id, DuplicateResolution.Distinct, item1.Id);

        // Assert
        _dbContext.DuplicateCandidates.Should().BeEmpty();

        var deletedItem2 = await _dbContext.Hashes.FindAsync(item2.Id);
        deletedItem2!.DeletedAt.Should().NotBeNull();

        _fileSystem.FileExists(dest2).Should().BeFalse();
    }
}
