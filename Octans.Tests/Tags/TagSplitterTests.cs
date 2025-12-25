using Octans.Core;
using Octans.Core.Tags;

namespace Octans.Tests.Tags;

public class TagSplitterTests
{
    private readonly TagSplitter _sut = new();

    [Fact]
    public void SplitTag_SimpleTag_ReturnsEmptySpaceAndTag()
    {
        var (space, subtag) = _sut.SplitTag("tag");

        Assert.Empty(space);
        Assert.Equal("tag", subtag);
    }

    [Fact]
    public void SplitTag_NamespacedTag_ReturnsNamespaceAndTag()
    {
        var (space, subtag) = _sut.SplitTag("namespace:tag");

        Assert.Equal("namespace", space);
        Assert.Equal("tag", subtag);
    }

    [Fact]
    public void SplitTag_MultipleNamespaces_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => _sut.SplitTag("namespace:sub:tag"));
    }

    [Fact]
    public void SplitTag_EmptyString_ReturnsEmptySpaceAndEmptyTag()
    {
        var (space, subtag) = _sut.SplitTag("");

        Assert.Empty(space);
        Assert.Empty(subtag);
    }
}
