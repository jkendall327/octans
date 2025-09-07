using Octans.Core.Models;

namespace Octans.Core.Models.Ratings;

public class HashRating
{
    public int Id { get; set; }
    public required HashItem Hash { get; set; }
    public required RatingSystem RatingSystem { get; set; }
    public int Value { get; set; }
}
