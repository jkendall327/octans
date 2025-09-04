using FluentAssertions;
using Octans.Core.Querying;

namespace Octans.Tests;

public class QueryParserTests
{
    private readonly QueryParser _parser = new();

    [Fact]
    public void Parse_SingleTagQuery_ReturnsTagPredicate()
    {
        var results = _parser.Parse(["character:mario"]);

        var result = results.OfType<TagPredicate>().Single();

        result.Should().Match<TagPredicate>(tp =>
            tp.NamespacePattern == "character" && tp.SubtagPattern == "mario" && !tp.IsExclusive);
    }

    [Fact]
    public void Parse_ExclusiveTagQuery_ReturnsExclusiveTagPredicate()
    {
        var results = _parser.Parse(["-character:bowser"]);

        var result = results.OfType<TagPredicate>().Single();

        result.Should().Match<TagPredicate>(tp =>
            tp.NamespacePattern == "character" && tp.SubtagPattern == "bowser" && tp.IsExclusive);
    }

    [Fact]
    public void Parse_WildcardTagQuery_ReturnsWildcardTagPredicate()
    {
        var results = _parser.Parse(["character:mario*"]);
        var result = results.OfType<TagPredicate>().Single();

        result.Should().Match<TagPredicate>(tp =>
            tp.NamespacePattern == "character" && tp.SubtagPattern == "mario*" && tp.IsWildcard());
    }

    [Fact]
    public void Parse_SystemQuery_ReturnsEverythingPredicate()
    {
        var result = _parser.Parse(["system:everything"]);

        result.Single().Should().BeOfType<EverythingPredicate>();
    }

    [Fact]
    public void Parse_OrQuery_ReturnsOrPredicate()
    {
        var results = _parser.Parse(["or:character:mario OR character:luigi"]);

        var result = results.OfType<OrPredicate>().Single();

        result.Predicates.Should().HaveCount(2).And.AllBeOfType<TagPredicate>();
    }

    [Fact]
    public void Parse_MultipleQueries_ReturnsMultiplePredicates()
    {
        var result = _parser.Parse(["character:mario", "-stage:mushroom_kingdom", "system:everything"]);

        result.Should().HaveCount(3);

        result[0].Should().BeOfType<TagPredicate>();
        result[1].Should().BeOfType<TagPredicate>().Which.IsExclusive.Should().BeTrue();
        result[2].Should().BeOfType<EverythingPredicate>();
    }

    [Fact]
    public void Parse_QueryWithExtraWhitespace_TrimsAndParsesCorrectly()
    {
        var results = _parser.Parse(["  character  :  mario  "]);

        var result = results.OfType<TagPredicate>().Single();

        result.Should().Match<TagPredicate>(tp => tp.NamespacePattern == "character" && tp.SubtagPattern == "mario");
    }

    [Fact]
    public void Parse_NestedOrQuery_ReturnsNestedOrPredicate()
    {
        var result = _parser.Parse(["or:character:mario OR (or:stage:mushroom_kingdom OR stage:bowser_castle)"]);

        var outer = result.OfType<OrPredicate>().Single();

        outer.Predicates.Should().HaveCount(2);

        var innerTag = outer.Predicates[0] as TagPredicate;

        innerTag.Should().Match<TagPredicate>(tp => tp.NamespacePattern == "character" && tp.SubtagPattern == "mario");

        var innerOr = outer.Predicates[1] as OrPredicate;

        innerOr!.Predicates.Should().HaveCount(2).And.AllBeOfType<TagPredicate>();
    }

    [Fact]
    public void Parse_OrQueryWithExclusivePredicate_ReturnsOrPredicateWithExclusiveTag()
    {
        var result = _parser.Parse(["or:character:mario OR -character:bowser"]);

        result.Should().HaveCount(1);

        result.OfType<OrPredicate>().Single().Predicates.Should().HaveCount(2);

        var inner = result[0] as OrPredicate;

        inner.Should().NotBeNull();

        inner!.Predicates.First().Should().Match<TagPredicate>(tp =>
                tp.NamespacePattern == "character" && tp.SubtagPattern == "mario" && !tp.IsExclusive);

        inner.Predicates.Last().Should().Match<TagPredicate>(tp =>
                tp.NamespacePattern == "character" && tp.SubtagPattern == "bowser" && tp.IsExclusive);
    }

    [Fact]
    public void Parse_ComplexNestedOrQueryWithExclusives_ReturnsCorrectStructure()
    {
        var query = "or:character:mario OR (or:-stage:bowser_castle OR character:luigi)";

        var result = _parser.Parse([query]).OfType<OrPredicate>().Single();

        result.Predicates.Should().HaveCount(2);

        result.Predicates.First().Should().Match<TagPredicate>(tp =>
            tp.NamespacePattern == "character" && tp.SubtagPattern == "mario" && !tp.IsExclusive);

        var inner = result.Predicates.Last() as OrPredicate;

        inner!.Predicates.Should().HaveCount(2);

        inner.Predicates.First().Should().Match<TagPredicate>(tp =>
                tp.NamespacePattern == "stage" && tp.SubtagPattern == "bowser_castle" && tp.IsExclusive);

        inner.Predicates.Last().Should().Match<TagPredicate>(tp =>
                tp.NamespacePattern == "character" && tp.SubtagPattern == "luigi" && !tp.IsExclusive);
    }

    [Fact]
    public void Parse_MultipleWildcards_CollapsesToSingleWildcard()
    {
        var result = _parser.Parse(["character:**samus**"]);

        var tag = result.OfType<TagPredicate>().Single();

        tag.NamespacePattern.Should().Be("character");
        tag.SubtagPattern.Should().Be("*samus*");
    }
}