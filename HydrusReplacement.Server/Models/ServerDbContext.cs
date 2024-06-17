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
}