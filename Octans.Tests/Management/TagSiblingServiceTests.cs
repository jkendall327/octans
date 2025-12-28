using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Octans.Client;
using Octans.Core;
using Octans.Core.Models;
using Octans.Core.Models.Tagging;
using Octans.Core.Tags;
using Xunit.Abstractions;

namespace Octans.Tests;

public class TagSiblingServiceTests : IAsyncLifetime, IClassFixture<DatabaseFixture>
{
    readonly IServiceProvider _provider;
    readonly TagSiblingService _sut;
    readonly DatabaseFixture _databaseFixture;

    public TagSiblingServiceTests(ITestOutputHelper testOutputHelper, DatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture;
        var services = new ServiceCollection();
        services.AddLogging(s => s.AddProvider(new XUnitLoggerProvider(testOutputHelper)));
        services.AddBusinessServices();
        databaseFixture.RegisterDbContext(services);
        _provider = services.BuildServiceProvider();
        _sut = _provider.GetRequiredService<TagSiblingService>();
    }

    [Fact]
    public async Task Resolve_ReplacesWithIdealTag()
    {
        await using var scope = _provider.CreateAsyncScope();
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
        await using var scope = _provider.CreateAsyncScope();
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
        await DatabaseFixture.ResetAsync(_provider);
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
