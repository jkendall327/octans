using System.ComponentModel.DataAnnotations;

namespace Octans.Data.Models;

public class Repository
{
    [Key]
    public int Id { get; set; }
    public required string Name { get; set; }
}
