using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Octans.Core.Downloads;
using Octans.Core.Downloaders;
using Octans.Core.Models;
using Xunit;

namespace Octans.Tests.Downloads;

public sealed class DatabaseDownloadQueueTests : IDisposable, IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly IDbContextFactory<ServerDbContext> _contextFactory = Substitute.For<IDbContextFactory<ServerDbContext>>();
    private readonly IBandwidthLimiter _bandwidthLimiter = Substitute.For<IBandwidthLimiter>();
    private readonly DatabaseDownloadQueue _sut;

    public DatabaseDownloadQueueTests()
    {
        // Create and open an in-memory SQLite connection
        _connection.Open();

        // Configure the context factory to use SQLite
        _contextFactory.CreateDbContextAsync(Arg.Any<CancellationToken>()).Returns(CreateContext());
        _contextFactory.CreateDbContextAsync().Returns(CreateContext());

        // Configure the bandwidth limiter
        _bandwidthLimiter.IsBandwidthAvailable(Arg.Any<string>()).Returns(true);

        // Create the system under test
        _sut = new DatabaseDownloadQueue(
            _contextFactory,
            _bandwidthLimiter,
            NullLogger<DatabaseDownloadQueue>.Instance);
    }

    private ServerDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ServerDbContext>()
            .UseSqlite(_connection)
            .Options;

        var context = new ServerDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public async Task EnqueueAsync_ShouldAddDownloadToQueue()
    {
        // Arrange
        var download = new QueuedDownload
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file.jpg",
            DestinationPath = "/downloads/file.jpg",
            Domain = string.Empty, // Will be set by the queue
            Priority = 5,
            QueuedAt = DateTime.UtcNow
        };

        // Act
        var id = await _sut.EnqueueAsync(download);

        // Assert
        Assert.Equal(download.Id, id);

        await using var context = await _contextFactory.CreateDbContextAsync();
        var savedDownload = await context.QueuedDownloads.FindAsync(id);
        
        Assert.NotNull(savedDownload);
        Assert.Equal(download.Url, savedDownload.Url);
        Assert.Equal(download.Priority, savedDownload.Priority);
        Assert.Equal("example.com", savedDownload.Domain);
    }

    [Fact]
    public async Task DequeueNextEligibleAsync_ShouldReturnHighestPriorityDownload()
    {
        // Arrange
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var download1 = new QueuedDownload
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file1.jpg",
            DestinationPath = "/downloads/file1.jpg",
            Domain = "example.com",
            Priority = 3,
            QueuedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        
        var download2 = new QueuedDownload
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file2.jpg",
            DestinationPath = "/downloads/file2.jpg",
            Domain = "example.com",
            Priority = 5,
            QueuedAt = DateTime.UtcNow
        };
        
        context.QueuedDownloads.AddRange(download1, download2);
        await context.SaveChangesAsync();

        // Act
        var result = await _sut.DequeueNextEligibleAsync(CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(download2.Id, result.Id); // Should get the higher priority one
        
        // Verify it was removed from the queue
        var remainingCount = await context.QueuedDownloads.CountAsync();
        Assert.Equal(1, remainingCount);
    }

    [Fact]
    public async Task DequeueNextEligibleAsync_ShouldRespectBandwidthLimits()
    {
        // Arrange
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var download = new QueuedDownload
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file.jpg",
            DestinationPath = "/downloads/file.jpg",
            Domain = "example.com",
            Priority = 5,
            QueuedAt = DateTime.UtcNow
        };
        
        context.QueuedDownloads.Add(download);
        await context.SaveChangesAsync();

        // Set bandwidth limiter to reject this domain
        _bandwidthLimiter.IsBandwidthAvailable("example.com").Returns(false);

        // Act
        var result = await _sut.DequeueNextEligibleAsync(CancellationToken.None);

        // Assert
        Assert.Null(result); // Should not dequeue anything
        
        // Verify it's still in the queue
        var remainingCount = await context.QueuedDownloads.CountAsync();
        Assert.Equal(1, remainingCount);
    }

    [Fact]
    public async Task GetQueuedCountAsync_ShouldReturnCorrectCount()
    {
        // Arrange
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var downloads = new[]
        {
            new QueuedDownload
            {
                Id = Guid.NewGuid(),
                Url = "https://example.com/file1.jpg",
                DestinationPath = "/downloads/file1.jpg",
                Domain = "example.com",
                Priority = 1,
                QueuedAt = DateTime.UtcNow
            },
            new QueuedDownload
            {
                Id = Guid.NewGuid(),
                Url = "https://example.com/file2.jpg",
                DestinationPath = "/downloads/file2.jpg",
                Domain = "example.com",
                Priority = 2,
                QueuedAt = DateTime.UtcNow
            }
        };
        
        context.QueuedDownloads.AddRange(downloads);
        await context.SaveChangesAsync();

        // Act
        var count = await _sut.GetQueuedCountAsync();

        // Assert
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task RemoveAsync_ShouldRemoveDownloadFromQueue()
    {
        // Arrange
        var downloadId = Guid.NewGuid();
        
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var download = new QueuedDownload
        {
            Id = downloadId,
            Url = "https://example.com/file.jpg",
            DestinationPath = "/downloads/file.jpg",
            Domain = "example.com",
            Priority = 5,
            QueuedAt = DateTime.UtcNow
        };
        
        context.QueuedDownloads.Add(download);
        await context.SaveChangesAsync();

        // Act
        await _sut.RemoveAsync(downloadId);

        // Assert
        var remainingDownload = await context.QueuedDownloads.FindAsync(downloadId);
        Assert.Null(remainingDownload);
    }

    [Fact]
    public async Task RemoveAsync_ShouldNotThrowWhenDownloadDoesNotExist()
    {
        // Act & Assert
        await _sut.RemoveAsync(Guid.NewGuid()); // Should not throw
    }

    [Fact]
    public async Task DequeueNextEligibleAsync_ShouldReturnOldestWhenPriorityEqual()
    {
        // Arrange
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var download1 = new QueuedDownload
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file1.jpg",
            DestinationPath = "/downloads/file1.jpg",
            Domain = "example.com",
            Priority = 5,
            QueuedAt = DateTime.UtcNow.AddMinutes(-5) // Older
        };
        
        var download2 = new QueuedDownload
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file2.jpg",
            DestinationPath = "/downloads/file2.jpg",
            Domain = "example.com",
            Priority = 5, // Same priority
            QueuedAt = DateTime.UtcNow // Newer
        };
        
        context.QueuedDownloads.AddRange(download1, download2);
        await context.SaveChangesAsync();

        // Act
        var result = await _sut.DequeueNextEligibleAsync(CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(download1.Id, result.Id); // Should get the older one
    }

    public void Dispose() => _connection.Dispose();

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
}
