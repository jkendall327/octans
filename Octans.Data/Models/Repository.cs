using System.ComponentModel.DataAnnotations;

namespace Octans.Core.Models;

public class Repository
{
    [Key]
    public int Id { get; set; }
    public required string Name { get; set; }
}
