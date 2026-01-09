using Microsoft.EntityFrameworkCore;
using Octans.Core.Downloaders;
using Octans.Core.Models.Tagging;
using Octans.Core.Repositories;
using Octans.Core.Models.Ratings;
using Octans.Core.Importing.Jobs;
using Octans.Core.Models.Duplicates;

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
    public virtual DbSet<SubscriptionExecution> SubscriptionExecutions { get; set; }
    public virtual DbSet<RatingSystem> RatingSystems { get; set; }
    public virtual DbSet<HashRating> HashRatings { get; set; }
    public virtual DbSet<ImportJob> ImportJobs { get; set; }
    public virtual DbSet<ImportItem> ImportItems { get; set; }
    public virtual DbSet<DuplicateCandidate> DuplicateCandidates { get; set; }
    public virtual DbSet<DuplicateDecision> DuplicateDecisions { get; set; }
    public virtual DbSet<Note> Notes { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<FileRecord>(entity =>
        {
            entity.HasIndex(e => e.Filepath).IsUnique();
            entity.Property(e => e.Filepath).HasMaxLength(4096);
        });

        modelBuilder.Entity<HashItem>(entity =>
        {
            entity.HasIndex(e => e.Hash).IsUnique();
            entity.Property(e => e.Hash).HasMaxLength(32);
            entity.HasIndex(e => e.PerceptualHash);
            entity.HasIndex(e => e.RepositoryId);
            entity.HasIndex(e => e.DeletedAt);
        });

        modelBuilder.Entity<Namespace>(entity =>
        {
            entity.HasIndex(e => e.Value).IsUnique();
            entity.Property(e => e.Value).HasMaxLength(100);
        });

        modelBuilder.Entity<Subtag>(entity =>
        {
            entity.HasIndex(e => e.Value).IsUnique();
            entity.Property(e => e.Value).HasMaxLength(255);
        });

        modelBuilder.Entity<Tag>(entity =>
        {
            entity.HasIndex(t => new { t.NamespaceId, t.SubtagId }).IsUnique();
        });

        modelBuilder.Entity<Mapping>(entity =>
        {
            entity.HasIndex(m => new { m.HashId, m.TagId }).IsUnique();
        });

        modelBuilder.Entity<QueuedDownload>(entity =>
        {
            entity.HasIndex(e => e.Url);
            entity.Property(e => e.Url).HasMaxLength(2048);
            entity.Property(e => e.DestinationPath).HasMaxLength(1024);
            entity.Property(e => e.Domain).HasMaxLength(255);
        });

        modelBuilder.Entity<ImportItem>(entity =>
        {
            entity.Property(e => e.Source).HasMaxLength(2048);
        });

        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.HasIndex(e => e.Name).IsUnique();
        });

        modelBuilder.Entity<DuplicateCandidate>()
            .HasOne(c => c.Hash1)
            .WithMany()
            .HasForeignKey(c => c.HashId1)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DuplicateCandidate>()
            .HasOne(c => c.Hash2)
            .WithMany()
            .HasForeignKey(c => c.HashId2)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DuplicateDecision>()
            .HasOne(d => d.Hash1)
            .WithMany()
            .HasForeignKey(d => d.HashId1)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DuplicateDecision>()
            .HasOne(d => d.Hash2)
            .WithMany()
            .HasForeignKey(d => d.HashId2)
            .OnDelete(DeleteBehavior.Cascade);

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