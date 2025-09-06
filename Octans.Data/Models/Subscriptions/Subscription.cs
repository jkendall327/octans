using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Octans.Core.Models;

public class Subscription
{
    [Key]
    public int Id { get; set; }
    public required string Name { get; set; }
    public TimeSpan CheckPeriod { get; set; }
    public required string Query { get; set; }
    public int ProviderId { get; set; }
    [ForeignKey(nameof(ProviderId))]
    public Provider Provider { get; set; } = null!;
    public DateTime NextCheck { get; set; }
}
