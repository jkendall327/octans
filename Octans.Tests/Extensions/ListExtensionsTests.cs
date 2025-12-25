using Octans.Core;
using Octans.Core.Querying;

namespace Octans.Tests.Extensions;

public class ListExtensionsTests
{
    private class PredicateA : IPredicate;
    private class PredicateB : IPredicate;
    private class PredicateC : IPredicate;
    private class PredicateD : IPredicate;

    [Fact]
    public void Partition_SplitsPredicatesByType()
    {
        var predicates = new List<IPredicate>
        {
            new PredicateA(),
            new PredicateB(),
            new PredicateC(),
            new PredicateA(),
            new PredicateC()
        };

        var (listA, listB, listC) = predicates.Partition<PredicateA, PredicateB, PredicateC>();

        Assert.Equal(2, listA.Count);
        Assert.Single(listB);
        Assert.Equal(2, listC.Count);
    }

    [Fact]
    public void Partition_EmptyList_ReturnsEmptyLists()
    {
        var predicates = new List<IPredicate>();

        var (listA, listB, listC) = predicates.Partition<PredicateA, PredicateB, PredicateC>();

        Assert.Empty(listA);
        Assert.Empty(listB);
        Assert.Empty(listC);
    }

    [Fact]
    public void Partition_WithUnmatchedType_IgnoresUnmatched()
    {
        var predicates = new List<IPredicate>
        {
            new PredicateA(),
            new PredicateD()
        };

        var (listA, listB, listC) = predicates.Partition<PredicateA, PredicateB, PredicateC>();

        Assert.Single(listA);
        Assert.Empty(listB);
        Assert.Empty(listC);
    }

    [Fact]
    public void Partition_InputIsNotModified()
    {
        var predicates = new List<IPredicate>
        {
            new PredicateA(),
            new PredicateB()
        };

        predicates.Partition<PredicateA, PredicateB, PredicateC>();

        Assert.Equal(2, predicates.Count);
    }
}
