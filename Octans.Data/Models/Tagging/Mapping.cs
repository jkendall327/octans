using System.ComponentModel.DataAnnotations.Schema;

namespace Octans.Core.Models.Tagging;

public class Mapping
{
    public int Id { get; set; }
    public int TagId { get; set; }
    [ForeignKey(nameof(TagId))]
    public required Tag Tag { get; set; }
    public int HashId { get; set; }
    [ForeignKey(nameof(HashId))]
    public required HashItem Hash { get; set; }
}