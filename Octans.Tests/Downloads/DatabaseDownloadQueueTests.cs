using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Octans.Core.Downloads;
using Octans.Core.Downloads.Bandwidth;
using Octans.Data.Models;
using Octans.Tests.Helpers;

namespace Octans.Tests.Downloads;

public sealed class DatabaseDownloadQueueTests : IDisposable, IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly IDbContextFactory<ServerDbContext> _contextFactory = Substitute.For<IDbContextFactory<ServerDbContext>>();
    private readonly IBandwidthLimiter _bandwidthLimiter = Substitute.For<IBandwidthLimiter>();
    private readonly IDownloadStateService _stateService = Substitute.For<IDownloadStateService>();
    private readonly DatabaseDownloadQueue _sut;

    public DatabaseDownloadQueueTests()
    {
        // Create and open an in-memory SQLite connection
        _connection.Open();

        // Configure the context factory to use SQLite
        _contextFactory.CreateDbContextAsync().ReturnsForAnyArgs(CreateContext());

        // Configure the bandwidth limiter
        _bandwidthLimiter.IsBandwidthAvailable(default!).ReturnsForAnyArgs(true);

        _sut = new(
            _contextFactory,
            _bandwidthLimiter,
            _stateService,
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
        var download = new QueuedDownload
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file.jpg",
            DestinationPath = "/downloads/file.jpg",
            Domain = string.Empty, // Will be set by the queue
            Priority = 5,
            QueuedAt = TestClock.UtcNow
        };

        var id = await _sut.EnqueueAsync(download);

        Assert.Equal(download.Id, id);

        await using var context = CreateContext();
        var savedDownload = await context.QueuedDownloads.FindAsync(id);

        Assert.NotNull(savedDownload);
        Assert.Equal(download.Url, savedDownload.Url);
        Assert.Equal(download.Priority, savedDownload.Priority);
        Assert.Equal("example.com", savedDownload.Domain);
    }

    [Fact]
    public async Task EnsureQueuedAsync_ShouldRestoreMissingQueueRowFromStatus()
    {
        var downloadId = Guid.NewGuid();
        var status = new DownloadStatus
        {
            Id = downloadId,
            Url = "https://example.com/file.jpg",
            Filename = "file.jpg",
            DisplayName = "Nice file",
            DestinationPath = "/downloads/file.jpg",
            Priority = 7,
            State = DownloadState.InProgress,
            LastUpdated = TestClock.UtcNow,
            Domain = "example.com",
            SourceType = "Import",
            SourceId = "import-1"
        };

        await _sut.EnsureQueuedAsync(status);

        await using var context = CreateContext();
        var savedDownload = await context.QueuedDownloads.FindAsync(downloadId);

        Assert.NotNull(savedDownload);
        Assert.Equal(status.Url, savedDownload.Url);
        Assert.Equal(status.DestinationPath, savedDownload.DestinationPath);
        Assert.Equal(status.DisplayName, savedDownload.DisplayName);
        Assert.Equal(status.Priority, savedDownload.Priority);
        Assert.Equal(status.Domain, savedDownload.Domain);
        Assert.Equal(status.SourceType, savedDownload.SourceType);
        Assert.Equal(status.SourceId, savedDownload.SourceId);
    }

    [Fact]
    public async Task EnsureQueuedAsync_ShouldNotDuplicateExistingQueueRow()
    {
        var downloadId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            context.QueuedDownloads.Add(new()
            {
                Id = downloadId,
                Url = "https://example.com/file.jpg",
                DestinationPath = "/downloads/file.jpg",
                QueuedAt = TestClock.UtcNow,
                Domain = "example.com"
            });
            await context.SaveChangesAsync();
        }

        var status = new DownloadStatus
        {
            Id = downloadId,
            Url = "https://example.com/file.jpg",
            Filename = "file.jpg",
            DestinationPath = "/downloads/file.jpg",
            State = DownloadState.Queued,
            LastUpdated = TestClock.UtcNow,
            Domain = "example.com"
        };

        await _sut.EnsureQueuedAsync(status);

        await using var verificationContext = CreateContext();
        var count = await verificationContext.QueuedDownloads.CountAsync(d => d.Id == downloadId);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task DequeueNextEligibleAsync_ShouldReturnHighestPriorityDownload()
    {
        await using var context = CreateContext();

        var download1 = new QueuedDownload
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file1.jpg",
            DestinationPath = "/downloads/file1.jpg",
            Domain = "example.com",
            Priority = 3,
            QueuedAt = TestClock.UtcNow.AddMinutes(-5)
        };

        var download2 = new QueuedDownload
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file2.jpg",
            DestinationPath = "/downloads/file2.jpg",
            Domain = "example.com",
            Priority = 5,
            QueuedAt = TestClock.UtcNow
        };

        context.QueuedDownloads.AddRange(download1, download2);
        await context.SaveChangesAsync();

        var result = await _sut.DequeueNextEligibleAsync(CancellationToken.None);

        // Should get the higher priority one
        Assert.NotNull(result);
        Assert.Equal(download2.Id, result.Id);

        // Verify it was removed from the queue
        var remainingCount = await context.QueuedDownloads.CountAsync();
        Assert.Equal(1, remainingCount);
    }

    [Fact]
    public async Task DequeueNextEligibleAsync_ShouldRespectBandwidthLimits()
    {
        await using var context = CreateContext();

        var download = new QueuedDownload
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file.jpg",
            DestinationPath = "/downloads/file.jpg",
            Domain = "example.com",
            Priority = 5,
            QueuedAt = TestClock.UtcNow
        };

        context.QueuedDownloads.Add(download);
        await context.SaveChangesAsync();

        // Set bandwidth limiter to reject this domain
        _bandwidthLimiter.IsBandwidthAvailable("example.com").Returns(false);
        _stateService.GetDownloadById(download.Id).Returns(new DownloadStatus
        {
            Id = download.Id,
            Url = download.Url,
            Filename = "file.jpg",
            DestinationPath = download.DestinationPath,
            State = DownloadState.Queued,
            Domain = download.Domain
        });

        var result = await _sut.DequeueNextEligibleAsync(CancellationToken.None);

        // Should not dequeue anything
        Assert.Null(result);

        // Verify it's still in the queue
        var remainingCount = await context.QueuedDownloads.CountAsync();
        Assert.Equal(1, remainingCount);
        await _stateService.Received(1).UpdateState(download.Id, DownloadState.WaitingForBandwidth);
    }

    [Fact]
    public async Task DequeueNextEligibleAsync_ShouldNotRepeatWaitingStateWhenAlreadyWaitingForBandwidth()
    {
        await using var context = CreateContext();

        var download = new QueuedDownload
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file.jpg",
            DestinationPath = "/downloads/file.jpg",
            Domain = "example.com",
            Priority = 5,
            QueuedAt = TestClock.UtcNow
        };

        context.QueuedDownloads.Add(download);
        await context.SaveChangesAsync();

        _bandwidthLimiter.IsBandwidthAvailable("example.com").Returns(false);
        _stateService.GetDownloadById(download.Id).Returns(new DownloadStatus
        {
            Id = download.Id,
            Url = download.Url,
            Filename = "file.jpg",
            DestinationPath = download.DestinationPath,
            State = DownloadState.WaitingForBandwidth,
            Domain = download.Domain
        });

        var result = await _sut.DequeueNextEligibleAsync(CancellationToken.None);

        Assert.Null(result);
        await _stateService.DidNotReceive().UpdateState(download.Id, DownloadState.WaitingForBandwidth);
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
                QueuedAt = TestClock.UtcNow
            },
            new QueuedDownload
            {
                Id = Guid.NewGuid(),
                Url = "https://example.com/file2.jpg",
                DestinationPath = "/downloads/file2.jpg",
                Domain = "example.com",
                Priority = 2,
                QueuedAt = TestClock.UtcNow
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

        await using var context = CreateContext();

        var download = new QueuedDownload
        {
            Id = downloadId,
            Url = "https://example.com/file.jpg",
            DestinationPath = "/downloads/file.jpg",
            Domain = "example.com",
            Priority = 5,
            QueuedAt = TestClock.UtcNow
        };

        context.QueuedDownloads.Add(download);
        await context.SaveChangesAsync();

        await _sut.RemoveAsync(downloadId);

        // Do not use FindAsync here as we want to bypass the cache of the EF Core change tracker.
        var remainingDownload = await context.QueuedDownloads.SingleOrDefaultAsync(d => d.Id == downloadId);
        Assert.Null(remainingDownload);
    }

    [Fact]
    public async Task RemoveAsync_ShouldNotThrowWhenDownloadDoesNotExist()
    {
        await _sut.RemoveAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task DequeueNextEligibleAsync_ShouldReturnOldestWhenPriorityEqual()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var download1 = new QueuedDownload
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file1.jpg",
            DestinationPath = "/downloads/file1.jpg",
            Domain = "example.com",
            Priority = 5,
            QueuedAt = TestClock.UtcNow.AddMinutes(-5) // Older
        };

        var download2 = new QueuedDownload
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file2.jpg",
            DestinationPath = "/downloads/file2.jpg",
            Domain = "example.com",
            Priority = 5, // Same priority
            QueuedAt = TestClock.UtcNow // Newer
        };

        context.QueuedDownloads.AddRange(download1, download2);
        await context.SaveChangesAsync();

        var result = await _sut.DequeueNextEligibleAsync(CancellationToken.None);

        Assert.NotNull(result);

        // Should get the older one
        Assert.Equal(download1.Id, result.Id);
    }

    public void Dispose() => _connection.Dispose();

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
}
