namespace HydrusReplacement.Server.Models.Tagging;

public class Tag
{
    public int Id { get; set; }
    public Namespace Namespace { get; set; }
    public Subtag Subtag { get; set; }
}