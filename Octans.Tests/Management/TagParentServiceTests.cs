using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Octans.Client;
using Octans.Core;
using Octans.Core.Models;
using Octans.Core.Models.Tagging;
using Octans.Core.Tags;
using Octans.Tests.Infrastructure;
using Xunit.Abstractions;

namespace Octans.Tests.Management;

public class TagParentServiceTests : IAsyncLifetime, IClassFixture<DatabaseFixture>
{
    private readonly TagParentService _sut;
    private readonly DatabaseFixture _fixture;
    private readonly ServiceProvider _provider;

    public TagParentServiceTests(ITestOutputHelper testOutputHelper, DatabaseFixture databaseFixture)
    {
        _fixture = databaseFixture;
        var services = new ServiceCollection();
        services.AddLogging(s => s.AddProvider(new XUnitLoggerProvider(testOutputHelper)));

        _fixture.RegisterDbContext(services);

        services.AddScoped<TagParentService>();

        _provider = services.BuildServiceProvider();
        _sut = _provider.GetRequiredService<TagParentService>();
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync(_provider);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AddParentAsync_AddsRelationship()
    {
        var child = new TagModel("ns", "child");
        var parent = new TagModel("ns", "parent");

        await _sut.AddParentAsync(child, parent);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        var relationship = await db.TagParents
            .Include(tp => tp.Child).ThenInclude(t => t.Subtag)
            .Include(tp => tp.Parent).ThenInclude(t => t.Subtag)
            .SingleAsync();

        relationship.Child.Subtag.Value.Should().Be("child");
        relationship.Parent.Subtag.Value.Should().Be("parent");
    }

    [Fact]
    public async Task AddParentAsync_DetectsCycles_Direct()
    {
        var tag1 = new TagModel("ns", "1");
        var tag2 = new TagModel("ns", "2");

        await _sut.AddParentAsync(tag1, tag2);

        // tag1 -> tag2
        // Trying to add tag2 -> tag1 should fail
        var act = () => _sut.AddParentAsync(tag2, tag1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cycle*");
    }

    [Fact]
    public async Task AddParentAsync_DetectsCycles_Indirect()
    {
        var tag1 = new TagModel("ns", "1");
        var tag2 = new TagModel("ns", "2");
        var tag3 = new TagModel("ns", "3");

        await _sut.AddParentAsync(tag1, tag2);
        await _sut.AddParentAsync(tag2, tag3);

        // tag1 -> tag2 -> tag3
        // Trying to add tag3 -> tag1 should fail
        var act = () => _sut.AddParentAsync(tag3, tag1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cycle*");
    }

    [Fact]
    public async Task GetDescendantsAsync_ReturnsAllDescendants()
    {
        var tag1 = new TagModel("ns", "1");
        var tag2 = new TagModel("ns", "2");
        var tag3 = new TagModel("ns", "3");
        var tag4 = new TagModel("ns", "4");

        // tag1 -> tag2 -> tag3
        // tag1 -> tag4
        // Logic in test setup was confusing in my head, let's clarify:
        // TagParents table: Child -> Parent
        // AddParentAsync(child, parent)
        // AddParentAsync(tag4, tag1) -> tag4 is child of tag1
        // AddParentAsync(tag2, tag1) -> tag2 is child of tag1
        // AddParentAsync(tag3, tag2) -> tag3 is child of tag2

        await _sut.AddParentAsync(tag4, tag1);
        await _sut.AddParentAsync(tag2, tag1);
        await _sut.AddParentAsync(tag3, tag2);

        // Get descendants of tag1 (Parent)
        // Should include tag2, tag3, tag4
        var descendants = await _sut.GetDescendantsAsync(tag1);

        descendants.Should().HaveCount(3);
        descendants.Should().Contain(t => t.Subtag == "2");
        descendants.Should().Contain(t => t.Subtag == "3");
        descendants.Should().Contain(t => t.Subtag == "4");
    }

    [Fact]
    public async Task RemoveParentAsync_RemovesRelationship()
    {
        var child = new TagModel("ns", "child");
        var parent = new TagModel("ns", "parent");

        await _sut.AddParentAsync(child, parent);
        await _sut.RemoveParentAsync(child, parent);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        db.TagParents.Should().BeEmpty();
    }
}
