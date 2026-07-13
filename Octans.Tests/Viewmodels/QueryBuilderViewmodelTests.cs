using FluentAssertions;
using NSubstitute;
using Octans.Client;
using Octans.Client.Components.Gallery;

namespace Octans.Tests.Viewmodels;

public class QueryBuilderViewmodelTests
{
    private readonly IOctansClient _client = Substitute.For<IOctansClient>();
    private readonly QueryBuilderViewmodel _sut;

    public QueryBuilderViewmodelTests()
    {
        _sut = new(_client);
    }

    [Fact]
    public async Task OnInputAsync_ShouldPopulateSuggestions()
    {
        _client
            .GetQueryLanguageSuggestionsAsync("go", cancellationToken: Arg.Any<CancellationToken>())
            .Returns([new("character:goku", "character:goku", "tag")]);

        var stateChanged = false;
        _sut.StateChanged += () =>
        {
            stateChanged = true;
            return Task.CompletedTask;
        };

        await _sut.OnInputAsync("go", debounceMs: 10);

        stateChanged.Should().BeTrue();
        _sut.Suggestions.Should().Contain(s => s.Value == "character:goku");
    }

    [Fact]
    public async Task OnInputAsync_ShouldClearSuggestions_WhenInputIsEmpty()
    {
        _client
            .GetQueryLanguageSuggestionsAsync("go", cancellationToken: Arg.Any<CancellationToken>())
            .Returns([new("character:goku", "character:goku", "tag")]);

        await _sut.OnInputAsync("go", debounceMs: 10);
        _sut.Suggestions.Should().NotBeEmpty();

        await _sut.OnInputAsync("", debounceMs: 10);

        _sut.Suggestions.Should().BeEmpty();
    }
}
