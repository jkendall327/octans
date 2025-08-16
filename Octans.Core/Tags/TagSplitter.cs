namespace Octans.Core.Tags;

public class TagSplitter
{
    public (string space, string subtag) SplitTag(string tag)
    {
        var split = tag.Split(PredicateConstants.NamespaceDelimiter);

        return split.Length switch
        {
            0 => throw new InvalidOperationException("Somehow, splitting a tag resulted in an empty array"),
            1 => (string.Empty, split.First()),
            2 => (split.First(), split.Last()),
            var _ => throw new InvalidOperationException("Splitting a tag resulted in >2 entries"),
        };
    }
}