using System.ComponentModel.DataAnnotations.Schema;

namespace Octans.Core.Models.Tagging;

public class Tag
{
    public int Id { get; set; }
    public int NamespaceId { get; set; }
    [ForeignKey(nameof(NamespaceId))]
    public required Namespace Namespace { get; set; }
    public int SubtagId { get; set; }
    [ForeignKey(nameof(SubtagId))]
    public required Subtag Subtag { get; set; }
}