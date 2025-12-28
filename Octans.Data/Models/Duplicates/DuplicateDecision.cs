using System.ComponentModel.DataAnnotations;
using Octans.Core.Models;

namespace Octans.Core.Models.Duplicates;

public class DuplicateDecision
{
    [Key]
    public int Id { get; set; }

    public int HashId1 { get; set; }
    public HashItem Hash1 { get; set; } = null!;

    public int HashId2 { get; set; }
    public HashItem Hash2 { get; set; } = null!;

    public DuplicateResolution Resolution { get; set; }
    public DateTime DecidedAt { get; set; }
}
