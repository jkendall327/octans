using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Octans.Core.Repositories;
using Octans.Core.Models.Ratings;

namespace Octans.Core.Models;

[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "DTO")]
public class HashItem
{
    [Key] public int Id { get; init; }
    public required byte[] Hash { get; init; }
    public DateTime? DeletedAt { get; set; }
    public int RepositoryId { get; set; } = (int)RepositoryType.Inbox;
    public Repository? Repository { get; init; }
    public ICollection<HashRating> Ratings { get; } = new List<HashRating>();
    public ICollection<Note> Notes { get; } = new List<Note>();

    public bool IsDeleted() => DeletedAt is not null;
}