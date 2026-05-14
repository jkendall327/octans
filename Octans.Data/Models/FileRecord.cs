using System.ComponentModel.DataAnnotations;

namespace Octans.Data.Models;

public class FileRecord
{
    [Key]
    public int Id { get; set; }
    public required string Filepath { get; set; }
}