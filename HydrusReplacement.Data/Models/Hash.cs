using System.ComponentModel.DataAnnotations;

namespace HydrusReplacement.Core.Models;

public class HashItem
{
    [Key] public int Id { get; set; }
    public byte[] Hash { get; set; }
    public DateTime? DeletedAt { get; set; }

    public bool IsDeleted() => DeletedAt is null;
}