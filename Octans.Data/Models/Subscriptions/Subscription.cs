using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Octans.Core.Models;

public class Subscription
{
    [Key]
    public int Id { get; init; }

    [MaxLength(100)]
    public required string Name { get; init; }
    public TimeSpan CheckPeriod { get; init; }

    [MaxLength(500)]
    public required string Query { get; init; }
    public int ProviderId { get; init; }
    [ForeignKey(nameof(ProviderId))]
    public Provider Provider { get; init; } = null!;
    public DateTime NextCheck { get; set; }
}
