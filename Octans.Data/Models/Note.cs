using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Octans.Data.Models;

public class Note
{
    [Key]
    public int Id { get; set; }

    public int HashItemId { get; set; }

    [ForeignKey(nameof(HashItemId))]
    public HashItem? HashItem { get; set; }

    public required string Content { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastModifiedAt { get; set; }
}
