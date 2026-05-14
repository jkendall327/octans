namespace Octans.Data.Models.Tagging;

public class Tag
{
    public int Id { get; set; }
    public required Namespace Namespace { get; set; }
    public required Subtag Subtag { get; set; }
}