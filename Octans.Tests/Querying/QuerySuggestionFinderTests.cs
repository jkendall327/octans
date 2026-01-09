using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Octans.Core.Models;
using Octans.Core.Models.Tagging;
using Octans.Core.Querying;
using Octans.Core.Tags;
using Xunit;

namespace Octans.Tests.Querying;

public class QuerySuggestionFinderTests : IAsyncLifetime, IClassFixture<DatabaseFixture>
{
    private readonly ServiceProvider _provider;
    private readonly QuerySuggestionFinder _sut;
    private readonly ServerDbContext _context;

    public QuerySuggestionFinderTests(DatabaseFixture fixture)
    {
        var services = new ServiceCollection();
        fixture.RegisterDbContext(services);

        services.AddScoped<TagSplitter>();
        services.AddScoped<QuerySuggestionFinder>();

        _provider = services.BuildServiceProvider();
        _context = _provider.GetRequiredService<ServerDbContext>();
        _sut = _provider.GetRequiredService<QuerySuggestionFinder>();
    }

    public async Task InitializeAsync()
    {
        await DatabaseFixture.ResetAsync(_provider);
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetAutocompleteTagIds_ReturnsCorrectTags_ForSubtagSearch()
    {
        // Arrange
        var ns = new Namespace { Value = "character" };
        var t1 = new Tag { Namespace = ns, Subtag = new Subtag { Value = "goku" } };
        var t2 = new Tag { Namespace = ns, Subtag = new Subtag { Value = "gohan" } };
        var t3 = new Tag { Namespace = ns, Subtag = new Subtag { Value = "vegeta" } };

        await _context.Namespaces.AddAsync(ns);
        await _context.Tags.AddRangeAsync(t1, t2, t3);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetAutocompleteTagIds("go", false);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(t1);
        result.Should().Contain(t2);
        result.Should().NotContain(t3);
    }

    [Fact]
    public async Task GetAutocompleteTagIds_ReturnsCorrectTags_ForExactNamespaceSearch()
    {
        // Arrange
        var ns1 = new Namespace { Value = "artist" };
        var ns2 = new Namespace { Value = "character" };

        // Use different subtag values to avoid unique constraint violation on Subtags.Value
        var t1 = new Tag { Namespace = ns1, Subtag = new Subtag { Value = "test1" } };
        var t2 = new Tag { Namespace = ns2, Subtag = new Subtag { Value = "test2" } };

        await _context.Namespaces.AddRangeAsync(ns1, ns2);
        await _context.Tags.AddRangeAsync(t1, t2);
        await _context.SaveChangesAsync();

        // Act
        // Searching for "artist:te" should match "artist:test1"
        var result = await _sut.GetAutocompleteTagIds("artist:te", false);

        // Assert
        result.Should().HaveCount(1);
        result.Should().Contain(t1);
    }

    [Fact]
    public async Task GetAutocompleteTagIds_ReturnsCorrectTags_ForPartialNamespaceSearch()
    {
        // Arrange
        var ns1 = new Namespace { Value = "artist" };
        var ns2 = new Namespace { Value = "artwork" };
        var ns3 = new Namespace { Value = "character" };

        // Use different subtag values to avoid unique constraint violation on Subtags.Value
        var t1 = new Tag { Namespace = ns1, Subtag = new Subtag { Value = "test1" } };
        var t2 = new Tag { Namespace = ns2, Subtag = new Subtag { Value = "test2" } };
        var t3 = new Tag { Namespace = ns3, Subtag = new Subtag { Value = "test3" } };

        await _context.Namespaces.AddRangeAsync(ns1, ns2, ns3);
        await _context.Tags.AddRangeAsync(t1, t2, t3);
        await _context.SaveChangesAsync();

        // Act
        // Searching for "art:te" should match "artist:test1" and "artwork:test2"
        var result = await _sut.GetAutocompleteTagIds("art:te", false);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(t1);
        result.Should().Contain(t2);
        result.Should().NotContain(t3);
    }
}
