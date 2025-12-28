using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Octans.Client.Components.Gallery;
using Octans.Core.Models;
using Octans.Core.Models.Tagging;
using Octans.Core.Querying;
using Octans.Core.Tags;
using Xunit;

namespace Octans.Tests.Viewmodels;

public class QueryBuilderViewmodelTests : IAsyncLifetime, IClassFixture<DatabaseFixture>
{
    private readonly ServiceProvider _provider;
    private readonly QueryBuilderViewmodel _sut;
    private readonly ServerDbContext _context;

    public QueryBuilderViewmodelTests(DatabaseFixture fixture)
    {
        var services = new ServiceCollection();
        fixture.RegisterDbContext(services);

        services.AddScoped<TagSplitter>();
        services.AddScoped<QuerySuggestionFinder>();
        services.AddScoped<QueryBuilderViewmodel>();

        _provider = services.BuildServiceProvider();
        _context = _provider.GetRequiredService<ServerDbContext>();
        _sut = _provider.GetRequiredService<QueryBuilderViewmodel>();
    }

    public async Task InitializeAsync()
    {
        await DatabaseFixture.ResetAsync(_provider);
    }

    public Task DisposeAsync()
    {
        _sut.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task OnInputAsync_ShouldPopulateSuggestions()
    {
        // Arrange
        var ns = new Namespace { Value = "character" };
        var t1 = new Tag { Namespace = ns, Subtag = new Subtag { Value = "goku" } };

        await _context.Namespaces.AddAsync(ns);
        await _context.Tags.AddAsync(t1);
        await _context.SaveChangesAsync();

        bool stateChanged = false;
        _sut.StateChanged = () => { stateChanged = true; return Task.CompletedTask; };

        // Act
        // Use a short debounce for testing
        await _sut.OnInputAsync("go", debounceMs: 10);

        // Wait for debounce
        await Task.Delay(50);

        // Assert
        stateChanged.Should().BeTrue();
        _sut.Suggestions.Should().Contain(t1);
    }

    [Fact]
    public async Task OnInputAsync_ShouldClearSuggestions_WhenInputIsEmpty()
    {
        // Arrange
        var ns = new Namespace { Value = "character" };
        var t1 = new Tag { Namespace = ns, Subtag = new Subtag { Value = "goku" } };

        await _context.Namespaces.AddAsync(ns);
        await _context.Tags.AddAsync(t1);
        await _context.SaveChangesAsync();

        // Populate first
        await _sut.OnInputAsync("go", debounceMs: 10);
        await Task.Delay(50);
        _sut.Suggestions.Should().NotBeEmpty();

        // Act
        await _sut.OnInputAsync("", debounceMs: 10);
        await Task.Delay(50);

        // Assert
        _sut.Suggestions.Should().BeEmpty();
    }
}
