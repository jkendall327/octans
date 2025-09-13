namespace Octans.Core.Models.Tagging;

public class TagSibling
{
    public int Id { get; set; }

    public required Tag NonIdeal { get; set; }
    public required Tag Ideal { get; set; }

    // TODO: figure out what this is for in Hydrus and remove if unnecessary.
    public int Status { get; set; }
}