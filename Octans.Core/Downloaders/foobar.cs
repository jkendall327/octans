namespace Octans.Core.Downloaders;

using System;
using Microsoft.EntityFrameworkCore;

public enum DownloadState
{
    Queued,
    WaitingForBandwidth,
    InProgress,
    Paused,
    Completed,
    Failed,
    Canceled
}

public class DownloadStatus
{
    public Guid Id { get; set; }
    public required string Url { get; set; }
    public required string Filename { get; set; }
    public required string DestinationPath { get; set; }
    public long TotalBytes { get; set; }
    public long BytesDownloaded { get; set; }
    public double ProgressPercentage => TotalBytes > 0 ? (double)BytesDownloaded / TotalBytes * 100 : 0;
    public double CurrentSpeed { get; set; } // bytes per second
    public DownloadState State { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime LastUpdated { get; set; }
    public string? ErrorMessage { get; set; }
    public required string Domain { get; set; }
}

public class DownloadRequest
{
    public required string Url { get; set; }
    public required string DestinationPath { get; set; }
    public int Priority { get; set; } = 0; // Higher numbers = higher priority
}

public class QueuedDownload
{
    public Guid Id { get; set; }
    public required string Url { get; set; }
    public required string DestinationPath { get; set; }
    public DateTime QueuedAt { get; set; }
    public int Priority { get; set; }
    public required string Domain { get; set; }
}

// 2. Database Context

public class DownloadDbContext : DbContext
{
    public DownloadDbContext(DbContextOptions<DownloadDbContext> options) : base(options) { }
    
    public DbSet<QueuedDownload> QueuedDownloads { get; set; }
    public DbSet<DownloadStatus> DownloadStatuses { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.Entity<QueuedDownload>().HasKey(d => d.Id);
        modelBuilder.Entity<DownloadStatus>().HasKey(d => d.Id);
    }
}