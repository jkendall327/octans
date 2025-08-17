using Octans.Client.Components.Gallery;

namespace Octans.Client.Components.Imports;

public enum TagScope
{
    AllPaths,
    SelectedPaths
}

public sealed class TagAssignment
{
    public required TagViewer.Tag Tag { get; set; }
    public required TagScope Scope { get; set; }
    public List<string> SpecificPaths { get; } = [];
}

public sealed class TagAssignmentCollection
{
    public List<TagAssignment> Assignments { get; } = [];

    public void AddTagForAllPaths(TagViewer.Tag tag)
    {
        Assignments.Add(new TagAssignment
        {
            Tag = tag,
            Scope = TagScope.AllPaths
        });
    }

    public void AddTagForSelectedPaths(TagViewer.Tag tag, IEnumerable<string> paths)
    {
        var assignment = new TagAssignment
        {
            Tag = tag,
            Scope = TagScope.SelectedPaths
        };
        assignment.SpecificPaths.AddRange(paths);
        Assignments.Add(assignment);
    }

    public void RemoveTag(TagViewer.Tag tag)
    {
        Assignments.RemoveAll(a =>
            a.Tag.Namespace == tag.Namespace &&
            a.Tag.Subtag == tag.Subtag);
    }

    public List<TagViewer.Tag> GetTagsForPath(string path, IEnumerable<string> allPaths)
    {
        var tags = new List<TagViewer.Tag>();

        foreach (var assignment in Assignments)
        {
            if (assignment.Scope == TagScope.AllPaths)
            {
                tags.Add(assignment.Tag);
            }
            else if (assignment.Scope == TagScope.SelectedPaths &&
                     assignment.SpecificPaths.Contains(path))
            {
                tags.Add(assignment.Tag);
            }
        }

        return tags;
    }
}