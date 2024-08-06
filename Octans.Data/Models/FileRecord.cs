using System.ComponentModel.DataAnnotations;

namespace Octans.Core.Models;

public class FileRecord
{
    [Key]
    public int Id { get; set; }
    public string Filepath { get; set; }
}