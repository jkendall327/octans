namespace Octans.Core.Models.Tagging;

public class TagParent
{
    public int Id { get; set; }

    public required Tag Child { get; set; }
    public required Tag Parent { get; set; }
    
    // TODO: figure out what this is for in Hydrus and remove if unnecessary.
    public int Status { get; set; }
}