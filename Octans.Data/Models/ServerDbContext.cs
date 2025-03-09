using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Octans.Core.Downloaders;
using Octans.Core.Models.Tagging;

namespace Octans.Core.Models;

public class ServerDbContext : DbContext, IDesignTimeDbContextFactory<ServerDbContext>
{
    public ServerDbContext(DbContextOptions<ServerDbContext> context) : base(context)
    {

    }

    public ServerDbContext()
    {
        
    }
    
    public ServerDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ServerDbContext>();
        optionsBuilder.UseSqlite("Data Source=fake.db");

        return new(optionsBuilder.Options);
    }

    public virtual DbSet<FileRecord> FileRecords { get; set; }
    public virtual DbSet<HashItem> Hashes { get; set; }
    public virtual DbSet<Tag> Tags { get; set; }
    public virtual DbSet<Namespace> Namespaces { get; set; }
    public virtual DbSet<Subtag> Subtags { get; set; }
    public virtual DbSet<Mapping> Mappings { get; set; }
    public virtual DbSet<TagParent> TagParents { get; set; }
    public virtual DbSet<TagSibling> TagSiblings { get; set; }
    public virtual DbSet<QueuedDownload> QueuedDownloads { get; set; }
    public virtual DbSet<DownloadStatus> DownloadStatuses { get; set; }
}