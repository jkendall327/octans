using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Octans.Core.Repositories;
using Octans.Data.Models;
using Octans.Tests.Helpers;
using Octans.Tests.Infrastructure;

namespace Octans.Tests.Repositories;

public sealed class RepositoryChangeProcessorTests : IAsyncLifetime, IClassFixture<DatabaseFixture>
{
    private readonly IServiceProvider _provider;
    private readonly SpyProgressReporter _progressReporter = new();

    public RepositoryChangeProcessorTests(DatabaseFixture databaseFixture)
    {
        var services = new ServiceCollection();

        databaseFixture.RegisterDbContext(services);

        _provider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task ProcessBatch_updates_matching_hash_repositories()
    {
        await using var setupScope = _provider.CreateAsyncScope();
        var setupDb = setupScope.ServiceProvider.GetRequiredService<ServerDbContext>();
        setupDb.Hashes.Add(new HashItem
        {
            Hash = [0x01, 0x02, 0x03],
            RepositoryId = (int)RepositoryType.Inbox
        });
        await setupDb.SaveChangesAsync();

        var sut = CreateSut();

        await sut.ProcessBatch(
        [
            new("010203", RepositoryDestination.Archive),
            new("040506", RepositoryDestination.Trash)
        ]);

        await using var assertScope = _provider.CreateAsyncScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<ServerDbContext>();
        var hash = await assertDb.Hashes.SingleAsync(h => h.Hash == new byte[] { 0x01, 0x02, 0x03 });

        hash.RepositoryId.Should().Be((int)RepositoryType.Archive);
        _progressReporter.Starts.Should().ContainSingle(s =>
            s.Operation == "Repository changes" && s.TotalItems == 2);
        _progressReporter.Reports.Select(r => r.Processed).Should().Equal(1);
        _progressReporter.Completes.Should().ContainSingle();
    }

    [Fact]
    public async Task ProcessBatch_completes_progress_when_batch_is_empty()
    {
        var sut = CreateSut();

        await sut.ProcessBatch([]);

        _progressReporter.Starts.Should().ContainSingle(s =>
            s.Operation == "Repository changes" && s.TotalItems == 0);
        _progressReporter.Reports.Should().BeEmpty();
        _progressReporter.Completes.Should().ContainSingle();
    }

    public async Task InitializeAsync()
    {
        await DatabaseFixture.ResetAsync(_provider);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private RepositoryChangeProcessor CreateSut() => new(
        _provider.GetRequiredService<IDbContextFactory<ServerDbContext>>(),
        _progressReporter,
        NullLogger<RepositoryChangeProcessor>.Instance);
}
