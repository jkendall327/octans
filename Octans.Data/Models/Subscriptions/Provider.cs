using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

namespace Octans.Core.Models;

public class Provider
{
    [Key]
    public int Id { get; set; }
    public required string Name { get; set; }
    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
}
