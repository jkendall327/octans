using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Octans.Core.Tags;
using Octans.Data.Models;
using Octans.Data.Models.Tagging;
using Octans.Tests.Helpers;
using Xunit.Abstractions;

namespace Octans.Tests.Management;

public class TagSiblingServiceTests : IAsyncLifetime, IClassFixture<DatabaseFixture>
{
    private readonly OctansTestHost _host;
    private readonly TagSiblingService _sut;

    public TagSiblingServiceTests(ITestOutputHelper testOutputHelper, DatabaseFixture databaseFixture)
    {
        _host = OctansTestHost.Create(testOutputHelper, databaseFixture);
        _sut = _host.GetRequiredService<TagSiblingService>();
    }

    [Fact]
    public async Task Resolve_ReplacesWithIdealTag()
    {
        await using var scope = _host.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();

        var nonIdeal = new Tag
        {
            Namespace = new() { Value = string.Empty },
            Subtag = new() { Value = "catgirl" }
        };

        var ideal = new Tag
        {
            Namespace = new() { Value = string.Empty },
            Subtag = new() { Value = "nekomimi" }
        };

        db.Tags.AddRange(nonIdeal, ideal);
        db.TagSiblings.Add(new() { NonIdeal = nonIdeal, Ideal = ideal });
        await db.SaveChangesAsync();

        var tags = new[] { TagModel.WithoutNamespace("catgirl") };

        var resolved = await _sut.Resolve(tags);

        resolved.Should().ContainSingle(r => r.Tag.Subtag == "catgirl" && r.Display.Subtag == "nekomimi");
    }

    [Fact]
    public async Task Resolve_NoSibling_ReturnsOriginal()
    {
        await using var scope = _host.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();

        var tag = new Tag
        {
            Namespace = new() { Value = string.Empty },
            Subtag = new() { Value = "orphan" }
        };

        db.Tags.Add(tag);
        await db.SaveChangesAsync();

        var tags = new[] { TagModel.WithoutNamespace("orphan") };

        var resolved = await _sut.Resolve(tags);

        resolved.Should().ContainSingle(r => r.Tag.Subtag == "orphan" && r.Display.Subtag == "orphan");
    }

    public async Task InitializeAsync()
    {
        await _host.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => _host.DisposeAsync().AsTask();
}
