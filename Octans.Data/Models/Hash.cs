using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Octans.Core.Models;

[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "DTO")]
public class HashItem
{
    [Key] public int Id { get; set; }
    public required byte[] Hash { get; set; }
    public DateTime? DeletedAt { get; set; }

    public bool IsDeleted() => DeletedAt is not null;
}