using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Octans.Data.Models.Duplicates;
using Octans.Data.Models.Importing;
using Octans.Data.Models.Maintenance;
using Octans.Data.Models.Ratings;
using Octans.Data.Models.Subscriptions;
using Octans.Data.Models.Tagging;

namespace Octans.Data.Models;

public class ServerDbContext(DbContextOptions<ServerDbContext> context) : DbContext(context)
{
    private static readonly ValueConverter<DateTimeOffset, string> DateTimeOffsetConverter = new(
        value => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        value => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

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
    public virtual DbSet<StorageMaintenanceJob> StorageMaintenanceJobs { get; set; }
    public virtual DbSet<StorageMaintenanceFinding> StorageMaintenanceFindings { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

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

        modelBuilder.Entity<Subscription>()
            .Property(s => s.RepositoryId)
            .HasDefaultValue((int)RepositoryType.Inbox);

        modelBuilder.Entity<StorageMaintenanceJob>()
            .HasOne(j => j.SourceScanJob)
            .WithMany()
            .HasForeignKey(j => j.SourceScanJobId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<StorageMaintenanceFinding>()
            .HasOne(f => f.ScanJob)
            .WithMany(j => j.Findings)
            .HasForeignKey(f => f.ScanJobId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<StorageMaintenanceFinding>()
            .HasOne(f => f.RepairJob)
            .WithMany()
            .HasForeignKey(f => f.RepairJobId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<StorageMaintenanceJob>()
            .HasIndex(j => new { j.Status, j.CreatedAt });

        modelBuilder.Entity<StorageMaintenanceFinding>()
            .HasIndex(f => new { f.ScanJobId, f.Resolution, f.Type });

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

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(DateTimeOffsetConverter);
                }
            }
        }
    }
}
