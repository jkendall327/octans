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

        var emptyNs = new Namespace { Value = string.Empty };

        var nonIdeal = new Tag
        {
            Namespace = emptyNs,
            Subtag = new() { Value = "catgirl" }
        };

        var ideal = new Tag
        {
            Namespace = emptyNs,
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

        // Ensure namespace is unique if it exists or reuse?
        // In this test, it's a fresh DB context per test run due to DatabaseFixture reset,
        // BUT within one test execution we might need care.
        // Actually, TagSiblingServiceTests uses DatabaseFixture.ResetAsync, which clears tables.
        // However, if "string.Empty" namespace is created twice in two different tests, parallel execution might hurt,
        // but XUnit runs classes in parallel, methods sequentially by default? No, collections are parallel.
        // Let's safe-guard by checking existence or using a unique object reference if creating multiple tags in one test.
        // In Resolve_ReplacesWithIdealTag, we created two tags with "string.Empty" namespace.
        // If we created `new Namespace { Value = "" }` twice, EF would try to insert two rows with same value.
        // So sharing the `emptyNs` object reference above is key.

        // For this test, there is only one tag, so it should be fine as is, assuming ResetAsync works.
        // But to be safe and consistent with the fix above:

        var existingNs = db.Namespaces.FirstOrDefault(n => n.Value == string.Empty);
        if (existingNs != null)
        {
             tag.Namespace = existingNs;
        }

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
