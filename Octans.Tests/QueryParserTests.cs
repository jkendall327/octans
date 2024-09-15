using FluentAssertions;
using Octans.Core;
using Octans.Core.Querying;

namespace Octans.Tests;

public class QueryParserTests
{
    private readonly QueryParser _parser;

    public QueryParserTests()
    {
        _parser = new QueryParser();
    }

    [Fact]
    public void Parse_SingleTagQuery_ReturnsTagPredicate()
    {
        var result = _parser.Parse(["character:mario"]);

        result.Single()
            .Should().BeOfType<TagPredicate>()
            .Which.Should()
            .Match<TagPredicate>(tp => tp.NamespacePattern == "character" && tp.SubtagPattern == "mario" && !tp.IsExclusive);
    }

    [Fact]
    public void Parse_ExclusiveTagQuery_ReturnsExclusiveTagPredicate()
    {
        var result = _parser.Parse(["-character:bowser"]);

        result.Should().HaveCount(1);
        result[0].Should().BeOfType<TagPredicate>()
            .Which.Should().Match<TagPredicate>(tp =>
                tp.NamespacePattern == "character" &&
                tp.SubtagPattern == "bowser" &&
                tp.IsExclusive);
    }

    [Fact]
    public void Parse_WildcardTagQuery_ReturnsWildcardTagPredicate()
    {
        var result = _parser.Parse(["character:mario*"]);

        result.Should().HaveCount(1);
        result[0].Should().BeOfType<TagPredicate>()
            .Which.Should().Match<TagPredicate>(tp =>
                tp.NamespacePattern == "character" &&
                tp.SubtagPattern == "mario*" &&
                tp.IsWildcard());
    }

    [Fact]
    public void Parse_SystemQuery_ReturnsEverythingPredicate()
    {
        var result = _parser.Parse(["system:everything"]);

        result.Should().HaveCount(1);
        result[0].Should().BeOfType<EverythingPredicate>();
    }

    [Fact]
    public void Parse_OrQuery_ReturnsOrPredicate()
    {
        var result = _parser.Parse(["or:character:mario OR character:luigi"]);

        result.Should().HaveCount(1);
        result[0].Should().BeOfType<OrPredicate>()
            .Which.Predicates.Should().HaveCount(2)
            .And.AllBeOfType<TagPredicate>();
    }

    [Fact]
    public void Parse_MultipleQueries_ReturnsMultiplePredicates()
    {
        var result = _parser.Parse(["character:mario", "-stage:mushroom_kingdom", "system:everything"]);

        result.Should().HaveCount(3);
        result[0].Should().BeOfType<TagPredicate>();
        result[1].Should().BeOfType<TagPredicate>()
            .Which.IsExclusive.Should().BeTrue();
        result[2].Should().BeOfType<EverythingPredicate>();
    }

    [Fact]
    public void Parse_QueryWithExtraWhitespace_TrimsAndParsesCorrectly()
    {
        var result = _parser.Parse(["  character  :  mario  "]);

        result.Should().HaveCount(1);
        result[0].Should().BeOfType<TagPredicate>()
            .Which.Should().Match<TagPredicate>(tp =>
                tp.NamespacePattern == "character" &&
                tp.SubtagPattern == "mario");
    }
}