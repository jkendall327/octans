using System.ComponentModel.DataAnnotations;

namespace Octans.Core.Models;

public class HashItem
{
    [Key] public int Id { get; set; }
    public required byte[] Hash { get; set; }
    public DateTime? DeletedAt { get; set; }

    public bool IsDeleted() => DeletedAt is not null;
}