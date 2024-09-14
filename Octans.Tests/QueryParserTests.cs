using FluentAssertions;
using Octans.Core;
using Octans.Core.Querying;

namespace Octans.Tests;

public class QueryParserTests
{
    private readonly QueryParser _sut;

    public QueryParserTests()
    {
        _sut = new QueryParser();
    }

    /*[Fact]
    public async Task SingleFullyFormedTag()
    {
        var result = await _sut.Parse([ "character:samus aran"]);

        result.TagsToInclude.Should().Contain("character:samus aran");
    }
    
    [Theory]
    [InlineData("character:*")]
    [InlineData("character: *")]
    [InlineData("character:* ")]
    [InlineData("character: * ")]
    [InlineData(" character:*")]
    [InlineData("cHaracter:*")]
    [InlineData("CHARACTER:*")]
    public async Task SingleNamespace(string input)
    {
        var result = await _sut.Parse([input]);
        
        result.NamespacesToInclude.Should().Contain("character");
    }

    [Fact]
    public async Task NamespaceAndWildcard()
    {
        var result = await _sut.Parse(["character:samus*"]);
        
        result.NamespacesToInclude.Should().Contain("character");
        result.WildcardsToInclude.Should().Contain("samus*");
    }
    
    [Fact]
    public async Task WildcardInNamespace()
    {
        var result = await _sut.Parse(["char*:samus*"]);
        
        result.WildcardsToInclude.Should().Contain("char*:samus*");
    }
    
    [Fact]
    public async Task MultipleFullyFormedTags()
    {
        var result = await _sut.Parse([ "character:samus aran", "character:bayonetta", "series:animal crossing"]);
        
        result.TagsToInclude.Should().BeEquivalentTo("character:samus aran", "character:bayonetta", "series:animal crossing");
    }

    [Fact]
    public async Task SingleFullyFormedExclusion()
    {
        var result = await _sut.Parse([ "-character:samus aran"]);
        
        result.TagsToExclude.Should().Contain("character:samus aran");
    }
    
    [Fact]
    public async Task SingleFullyFormedORClause()
    {
        var result = await _sut.Parse([ "character:samus aran OR series:animal crossing"]);
        
        result.OrPredicates.Should().Contain("character:samus aran OR series:animal crossing");
        result.NamespacesToInclude.Should().BeEmpty();
        result.TagsToInclude.Should().BeEmpty();
    }*/

}