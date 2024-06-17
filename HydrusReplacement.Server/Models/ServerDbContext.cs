using HydrusReplacement.Server.Models.Tagging;
using Microsoft.EntityFrameworkCore;

namespace HydrusReplacement.Server.Models;

public class ServerDbContext : DbContext
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var dbFolder = Path.Join(AppDomain.CurrentDomain.BaseDirectory, "db");

        var db = Path.Join(dbFolder, "server.db");
        
        optionsBuilder.UseSqlite($"Data Source={db};");
        
        base.OnConfiguring(optionsBuilder);
    }
    
    public virtual DbSet<FileRecord> FileRecords { get; set; }
    public virtual DbSet<HashItem> Hashes { get; set; }
    public virtual DbSet<Tag> Tags { get; set; }
    public virtual DbSet<Namespace> Namespaces { get; set; }
    public virtual DbSet<Subtag> Subtags { get; set; }
    public virtual DbSet<Mapping> Mappings { get; set; }
    public virtual DbSet<TagParent> TagParents { get; set; }
    public virtual DbSet<TagSibling> TagSiblings { get; set; }
}