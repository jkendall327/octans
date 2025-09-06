using Microsoft.EntityFrameworkCore;
using Octans.Core.Downloaders;
using Octans.Core.Models.Tagging;

namespace Octans.Core.Models;

public class ServerDbContext(DbContextOptions<ServerDbContext> context) : DbContext(context)
{
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
    public virtual DbSet<Provider> Providers { get; set; }
    public virtual DbSet<Subscription> Subscriptions { get; set; }
}