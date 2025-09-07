using System.ComponentModel.DataAnnotations;

namespace Octans.Core.Models.Ratings;

public class RatingSystem
{
    [Key] public int Id { get; set; }
    public required string Name { get; set; }
    public RatingSystemType Type { get; set; }
    public int? MaxValue { get; set; }
    public ICollection<HashRating> HashRatings { get; set; } = new List<HashRating>();
}
