using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Octans.Core.Models;

public class Note
{
    [Key]
    public int Id { get; set; }

    public int HashItemId { get; set; }

    [ForeignKey(nameof(HashItemId))]
    public HashItem? HashItem { get; set; }

    public required string Content { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;
}
