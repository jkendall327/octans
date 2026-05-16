using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Octans.Core.Filesystem;
using Octans.Data.Models;
using Octans.Tests.Helpers;
using Xunit.Abstractions;

namespace Octans.Tests.Management;

public class FileDeleterTests : IAsyncLifetime, IClassFixture<DatabaseFixture>
{
    private readonly FileDeleter _sut;
    private readonly OctansTestHost _host;

    public FileDeleterTests(ITestOutputHelper testOutputHelper, DatabaseFixture databaseFixture)
    {
        _host = OctansTestHost.Create(testOutputHelper, databaseFixture);
        _sut = _host.GetRequiredService<FileDeleter>();
    }

    [Fact]
    public async Task Delete_ExistingFile_ReturnsSuccessAndRemovesFile()
    {
        await using var scope = _host.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();

        var stored = await _host.AddStoredImageAsync(TestingConstants.MinimalJpeg, new("jpeg", "image/jpeg"), db);

        var result = await _sut.ProcessDeletion([stored.HashItem.Id]);

        result.Single().Success.Should().BeTrue();

        // Ensure it's gone from the filesystem
        _host.FileSystem.FileExists(stored.Path).Should().BeFalse();

        // Ensure it's marked as deleted in the database
        var deletedHash = await db.Hashes.FindAsync(stored.HashItem.Id);
        await db.Entry(deletedHash!).ReloadAsync();

        deletedHash.Should().NotBeNull();
        deletedHash.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Delete_NonExistingFile_ReturnsNotFoundResult()
    {
        var response = await _sut.ProcessDeletion([888]);

        var result = response.Single();

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    public async Task InitializeAsync()
    {
        await _host.ResetDatabaseAsync();
        _host.EnsureImageStorage();
    }

    public Task DisposeAsync()
    {
        return _host.DisposeAsync().AsTask();
    }
}
