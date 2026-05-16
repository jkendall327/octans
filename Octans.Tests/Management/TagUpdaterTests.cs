using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Octans.Core.Tags;
using Octans.Data.Models;
using Octans.Data.Models.Tagging;
using Octans.Tests.Helpers;
using Xunit.Abstractions;

namespace Octans.Tests.Management;

public class TagUpdaterTests : IAsyncLifetime, IClassFixture<DatabaseFixture>
{
    private readonly TagUpdater _sut;
    private readonly OctansTestHost _host;

    public TagUpdaterTests(ITestOutputHelper testOutputHelper, DatabaseFixture databaseFixture)
    {
        _host = OctansTestHost.Create(testOutputHelper, databaseFixture);
        _sut = _host.GetRequiredService<TagUpdater>();
    }

    [Fact]
    public async Task UpdateTags_ValidRequest_ReturnsOk()
    {
        await using var scope = _host.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();

        var hash = await SetupInitialData(db);

        var request = new UpdateTagsRequest(hash.Id,
            [new("character", "samus aran")],
            [new("weapon", "laser")]);

        var response = await _sut.UpdateTags(request);

        response
            .Should()
            .Be(TagUpdateResult.TagsUpdated);

        var updatedTags = await db.Mappings
            .Where(m => m.Hash.Id == hash.Id)
            .Select(m => new
            {
                Namespace = m.Tag.Namespace.Value,
                Subtag = m.Tag.Subtag.Value
            })
            .ToListAsync();

        updatedTags.Should().ContainSingle(t => t.Namespace == "character" && t.Subtag == "samus aran");
        updatedTags.Should().NotContain(t => t.Namespace == "weapon" && t.Subtag == "laser");
    }

    [Fact]
    public async Task UpdateTags_InvalidHashId_ReturnsNotFound()
    {
        var tag = new TagModel("new", "tag");

        var request = new UpdateTagsRequest(999, [tag], []);

        var response = await _sut.UpdateTags(request);

        response
            .Should()
            .Be(TagUpdateResult.HashNotFound);
    }

    private static async Task<HashItem> SetupInitialData(ServerDbContext db)
    {
        var hash = new HashItem { Hash = [1, 2, 3, 4] };

        db.Hashes.Add(hash);

        var tag = new Tag
        {
            Namespace = new() { Value = "weapon" },
            Subtag = new() { Value = "laser" }
        };

        db.Tags.Add(tag);

        db.Mappings.Add(new()
        {
            Hash = hash,
            Tag = tag
        });

        await db.SaveChangesAsync();

        return hash;
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
