using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Octans.Data.Models.Ratings;

namespace Octans.Data.Models;

[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "DTO")]
public class HashItem
{
    [Key] public int Id { get; init; }
    public required byte[] Hash { get; init; }
    public string? Extension { get; set; }
    public string? ContentType { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RepositoryId { get; set; } = (int)RepositoryType.Inbox;
    public Repository? Repository { get; init; }
    public ICollection<HashRating> Ratings { get; } = new List<HashRating>();
    public ICollection<Note> Notes { get; } = new List<Note>();

    public ulong? PerceptualHash { get; set; }

    public bool IsDeleted() => DeletedAt is not null;
}
