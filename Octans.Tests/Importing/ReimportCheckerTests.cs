using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Octans.Core;
using Octans.Core.Filesystem;
using Octans.Core.Importing;
using Octans.Data.Models;
using Octans.Tests.Helpers;
using Xunit.Abstractions;

namespace Octans.Tests.Importing;

public class ReimportCheckerTests : IAsyncLifetime, IClassFixture<DatabaseFixture>
{
    private readonly OctansTestHost _host;
    private readonly ReimportChecker _sut;
    private readonly ServerDbContext _dbContext;

    public ReimportCheckerTests(ITestOutputHelper testOutputHelper, DatabaseFixture databaseFixture)
    {
        _host = OctansTestHost.Create(
            testOutputHelper,
            databaseFixture,
            dbLifetime: ServiceLifetime.Scoped);

        _dbContext = _host.GetRequiredService<ServerDbContext>();
        _sut = _host.GetRequiredService<ReimportChecker>();
    }

    public async Task InitializeAsync()
    {
        await _host.ResetDatabaseAsync();
        _host.EnsureImageStorage();
    }

    public Task DisposeAsync() => _host.DisposeAsync().AsTask();

    [Fact]
    public async Task CheckIfPreviouslyDeleted_RestoresContent_WhenContentMissing()
    {
        // Arrange
        var bytes = TestingConstants.MinimalJpeg;
        var hash = ContentHash.FromContent(bytes);
        var metadata = _host.GetRequiredService<ImageStorage>().GetMetadata(bytes);

        // Add hash to DB as deleted
        var hashItem = new HashItem
        {
            Hash = hash.Bytes,
            DeletedAt = TestClock.UtcNow.AddDays(-1)
        };
        _dbContext.Hashes.Add(hashItem);
        await _dbContext.SaveChangesAsync();

        // Ensure file is NOT on filesystem
        var destination = _host.GetRequiredService<ImageStorage>().GetOriginalDestination(hash, metadata);
        _host.FileSystem.FileExists(destination).Should().BeFalse();

        // Act
        var result = await _sut.CheckIfPreviouslyDeleted(hash, metadata, true, bytes);

        // Assert
        result!.Ok.Should().BeTrue();
        _host.FileSystem.FileExists(destination).Should().BeTrue();
    }
}
