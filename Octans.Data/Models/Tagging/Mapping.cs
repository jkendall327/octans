namespace Octans.Core.Models.Tagging;

public class Mapping
{
    public int Id { get; set; }
    public required Tag Tag { get; set; }
    public required HashItem Hash { get; set; }
}