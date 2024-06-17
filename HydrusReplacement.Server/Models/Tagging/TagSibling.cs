namespace HydrusReplacement.Server.Models.Tagging;

public class TagSibling
{
    public Tag Worse { get; set; }
    public Tag Better { get; set; }
    
    // TODO: figure out what this is for in Hydrus and remove if unnecessary.
    public int Status { get; set; }
}