using System.ComponentModel.DataAnnotations;

namespace HydrusReplacement.Server.Models;

public class FileRecord
{
    [Key]
    public int Id { get; set; }
    public string Filepath { get; set; }
}