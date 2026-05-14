using System.ComponentModel.DataAnnotations;

namespace Octans.Data.Models.Subscriptions;

public class Provider
{
    [Key]
    public int Id { get; set; }
    public required string Name { get; set; }
    public ICollection<Subscription> Subscriptions { get; } = new List<Subscription>();
}
