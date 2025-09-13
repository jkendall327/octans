using Microsoft.EntityFrameworkCore;
using Octans.Core.Downloaders;
using Octans.Core.Models.Tagging;
using Octans.Core.Repositories;
using Octans.Core.Models.Ratings;
using Octans.Core.Importing.Jobs;

namespace Octans.Core.Models;

public class ServerDbContext(DbContextOptions<ServerDbContext> context) : DbContext(context)
{
    public virtual DbSet<FileRecord> FileRecords { get; set; }
    public virtual DbSet<HashItem> Hashes { get; set; }
    public virtual DbSet<Repository> Repositories { get; set; }
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
    public virtual DbSet<RatingSystem> RatingSystems { get; set; }
    public virtual DbSet<HashRating> HashRatings { get; set; }
    public virtual DbSet<ImportJob> ImportJobs { get; set; }
    public virtual DbSet<ImportItem> ImportItems { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<HashRating>()
            .HasOne(r => r.Hash)
            .WithMany(h => h.Ratings);

        modelBuilder.Entity<HashRating>()
            .HasOne(r => r.RatingSystem)
            .WithMany(s => s.HashRatings);

        modelBuilder.Entity<Repository>().HasData(
            new Repository { Id = (int)RepositoryType.Inbox, Name = "Inbox" },
            new Repository { Id = (int)RepositoryType.Archive, Name = "Archive" },
            new Repository { Id = (int)RepositoryType.Trash, Name = "Trash" }
        );

        modelBuilder.Entity<RatingSystem>().HasData(
            new RatingSystem { Id = 1, Name = "Favourites", Type = RatingSystemType.Toggle, MaxValue = 1 },
            new RatingSystem { Id = 2, Name = "Quality", Type = RatingSystemType.Range, MaxValue = 5 }
        );
    }
}