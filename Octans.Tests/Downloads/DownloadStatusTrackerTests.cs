using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Octans.Core.Downloads;
using Octans.Core.Downloads.Models;
using Octans.Data.Models;
using Octans.Tests.Helpers;

namespace Octans.Tests.Downloads;

public sealed class DownloadStatusTrackerTests : IDisposable, IAsyncDisposable
{
    private readonly DbConnection _connection;
    private readonly DbContextOptions<ServerDbContext> _contextOptions;
    private readonly DownloadStatusTracker _service;

    public DownloadStatusTrackerTests()
    {
        // Create and open an in-memory SQLite database
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        // Configure the options to use the in-memory database
        _contextOptions = new DbContextOptionsBuilder<ServerDbContext>()
            .UseSqlite(_connection)
            .Options;

        // Create the schema in the database
        using (var context = new ServerDbContext(_contextOptions))
        {
            context.Database.EnsureCreated();
        }

        // Setup the mock context factory
        var contextFactory = Substitute.For<IDbContextFactory<ServerDbContext>>();

        contextFactory
            .CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(_ => new(_contextOptions));

        var timeProvider = new FakeTimeProvider(TestClock.UtcNow);
        _service = new(contextFactory, timeProvider, NullLogger<DownloadStatusTracker>.Instance);
    }

    [Fact]
    public async Task InitializeFromDbAsync_LoadsActiveDownloads()
    {
        var activeDownload = new DownloadStatus
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file.zip",
            Filename = "file.zip",
            DestinationPath = "/downloads/file.zip",
            Domain = "example.com",
            State = DownloadState.Queued,
            CreatedAt = TestClock.UtcNow,
            LastUpdated = TestClock.UtcNow
        };

        var completedDownload = new DownloadStatus
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/completed.zip",
            Filename = "completed.zip",
            DestinationPath = "/downloads/completed.zip",
            Domain = "example.com",
            State = DownloadState.Completed,
            CreatedAt = TestClock.UtcNow.AddHours(-1),
            LastUpdated = TestClock.UtcNow,
            CompletedAt = TestClock.UtcNow
        };

        await using (var context = new ServerDbContext(_contextOptions))
        {
            context.DownloadStatuses.Add(activeDownload);
            context.DownloadStatuses.Add(completedDownload);
            await context.SaveChangesAsync();
        }

        // Act
        await _service.InitializeFromDbAsync();

        // Assert
        var downloads = _service.GetAllDownloads();
        Assert.Single(downloads);
        Assert.Equal(activeDownload.Id, downloads[0].Id);
        Assert.DoesNotContain(downloads, d => d.Id == completedDownload.Id);
    }

    [Fact]
    public async Task GetDownloadById_ReturnsCorrectDownload()
    {
        var download = new DownloadStatus
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file.zip",
            Filename = "file.zip",
            DestinationPath = "/downloads/file.zip",
            Domain = "example.com",
            State = DownloadState.Queued,
            CreatedAt = TestClock.UtcNow,
            LastUpdated = TestClock.UtcNow
        };

        await _service.AddOrUpdateDownloadAsync(download);

        var result = _service.GetDownloadById(download.Id);

        Assert.NotNull(result);
        Assert.Equal(download.Id, result.Id);
        Assert.Equal(download.Url, result.Url);
    }

    [Fact]
    public void GetDownloadById_ReturnsNull_WhenIdNotFound()
    {
        var result = _service.GetDownloadById(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateProgress_UpdatesDownloadStatus()
    {
        // Arrange
        var download = new DownloadStatus
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file.zip",
            Filename = "file.zip",
            DestinationPath = "/downloads/file.zip",
            Domain = "example.com",
            State = DownloadState.InProgress,
            CreatedAt = TestClock.UtcNow,
            LastUpdated = TestClock.UtcNow,
            BytesDownloaded = 0,
            TotalBytes = 1000,
            CurrentSpeed = 0
        };

        await _service.AddOrUpdateDownloadAsync(download);

        DownloadStatusChanged? statusChanged = null;
        _service.DownloadStatusChanged += notification =>
        {
            statusChanged = notification;
            return ValueTask.CompletedTask;
        };

        // Act
        await _service.UpdateProgress(download.Id, 500, 1000, 100.0);

        var updated = _service.GetDownloadById(download.Id);
        Assert.NotNull(updated);
        Assert.Equal(500, updated.BytesDownloaded);
        Assert.Equal(1000, updated.TotalBytes);
        Assert.Equal(100.0, updated.CurrentSpeed);
        Assert.NotNull(statusChanged);
        Assert.Equal(500, statusChanged.Status.BytesDownloaded);
        Assert.Equal(1000, statusChanged.Status.TotalBytes);
        Assert.Equal(100.0, statusChanged.Status.CurrentSpeed);
    }

    [Fact]
    public async Task UpdateState_UpdatesStateAndRaisesEvents()
    {
        // Arrange
        var download = new DownloadStatus
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file.zip",
            Filename = "file.zip",
            DestinationPath = "/downloads/file.zip",
            Domain = "example.com",
            State = DownloadState.Queued,
            CreatedAt = TestClock.UtcNow,
            LastUpdated = TestClock.UtcNow
        };

        await _service.AddOrUpdateDownloadAsync(download);

        var downloadsChanged = 0;
        DownloadStatusChanged? statusChanged = null;
        _service.DownloadsChanged += _ =>
        {
            downloadsChanged++;
            return ValueTask.CompletedTask;
        };
        _service.DownloadStatusChanged += notification =>
        {
            statusChanged = notification;
            return ValueTask.CompletedTask;
        };

        // Act
        await _service.UpdateState(download.Id, DownloadState.InProgress);

        // Assert
        var updated = _service.GetDownloadById(download.Id);

        Assert.NotNull(updated);
        Assert.Equal(DownloadState.InProgress, updated.State);
        Assert.NotNull(updated.StartedAt);
        Assert.Equal(1, downloadsChanged);
        Assert.NotNull(statusChanged);
        Assert.Equal(DownloadState.InProgress, statusChanged.Status.State);
    }

    [Fact]
    public async Task UpdateState_SetsCompletedAt_WhenStateIsCompleted()
    {
        // Arrange
        var download = new DownloadStatus
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file.zip",
            Filename = "file.zip",
            DestinationPath = "/downloads/file.zip",
            Domain = "example.com",
            State = DownloadState.InProgress,
            CreatedAt = TestClock.UtcNow,
            LastUpdated = TestClock.UtcNow,
            StartedAt = TestClock.UtcNow
        };

        await _service.AddOrUpdateDownloadAsync(download);

        // Act
        await _service.UpdateState(download.Id, DownloadState.Completed);

        // Assert
        var updated = _service.GetDownloadById(download.Id);
        Assert.NotNull(updated);
        Assert.Equal(DownloadState.Completed, updated.State);
        Assert.NotNull(updated.CompletedAt);
    }

    [Fact]
    public async Task UpdateState_SetsErrorMessage_WhenStateIsFailed()
    {
        // Arrange
        var download = new DownloadStatus
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file.zip",
            Filename = "file.zip",
            DestinationPath = "/downloads/file.zip",
            Domain = "example.com",
            State = DownloadState.InProgress,
            CreatedAt = TestClock.UtcNow,
            LastUpdated = TestClock.UtcNow
        };

        await _service.AddOrUpdateDownloadAsync(download);

        // Act
        await _service.UpdateState(download.Id, DownloadState.Failed, "Download failed due to network error");

        // Assert
        var updated = _service.GetDownloadById(download.Id);
        Assert.NotNull(updated);
        Assert.Equal(DownloadState.Failed, updated.State);
        Assert.Equal("Download failed due to network error", updated.ErrorMessage);
    }

    [Fact]
    public async Task TryUpdateState_WhenCurrentStateMatches_UpdatesStatusAndDatabase()
    {
        var download = new DownloadStatus
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file.zip",
            Filename = "file.zip",
            DestinationPath = "/downloads/file.zip",
            Domain = "example.com",
            State = DownloadState.InProgress,
            BytesDownloaded = 1024,
            TotalBytes = 2048,
            CreatedAt = TestClock.UtcNow,
            LastUpdated = TestClock.UtcNow
        };

        await _service.AddOrUpdateDownloadAsync(download);

        var updated = await _service.TryUpdateState(
            download.Id,
            new HashSet<DownloadState> { DownloadState.InProgress },
            DownloadState.Completed);

        Assert.True(updated);
        Assert.Equal(DownloadState.Completed, _service.GetDownloadById(download.Id)?.State);

        await using var context = new ServerDbContext(_contextOptions);
        var savedStatus = await context.DownloadStatuses.FindAsync(download.Id);

        Assert.NotNull(savedStatus);
        Assert.Equal(DownloadState.Completed, savedStatus.State);
        Assert.Equal(1024, savedStatus.BytesDownloaded);
        Assert.Equal(2048, savedStatus.TotalBytes);
        Assert.NotNull(savedStatus.CompletedAt);
    }

    [Fact]
    public async Task TryUpdateState_WhenCurrentStateDoesNotMatch_LeavesPausedDownloadAlone()
    {
        var download = new DownloadStatus
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file.zip",
            Filename = "file.zip",
            DestinationPath = "/downloads/file.zip",
            Domain = "example.com",
            State = DownloadState.Paused,
            CreatedAt = TestClock.UtcNow,
            LastUpdated = TestClock.UtcNow
        };

        await _service.AddOrUpdateDownloadAsync(download);

        var updated = await _service.TryUpdateState(
            download.Id,
            new HashSet<DownloadState> { DownloadState.InProgress },
            DownloadState.Canceled);

        Assert.False(updated);
        Assert.Equal(DownloadState.Paused, _service.GetDownloadById(download.Id)?.State);

        await using var context = new ServerDbContext(_contextOptions);
        var savedStatus = await context.DownloadStatuses.FindAsync(download.Id);

        Assert.NotNull(savedStatus);
        Assert.Equal(DownloadState.Paused, savedStatus.State);
    }

    [Fact]
    public async Task AddOrUpdateDownload_AddsNewDownload()
    {
        // Arrange
        var download = new DownloadStatus
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file.zip",
            Filename = "file.zip",
            DestinationPath = "/downloads/file.zip",
            Domain = "example.com",
            State = DownloadState.Queued,
            CreatedAt = TestClock.UtcNow,
            LastUpdated = TestClock.UtcNow
        };

        // Act
        await _service.AddOrUpdateDownloadAsync(download);

        // Assert
        var result = _service.GetDownloadById(download.Id);
        Assert.NotNull(result);
        Assert.Equal(download.Id, result.Id);

        // Verify it was added to the database
        await using var context = new ServerDbContext(_contextOptions);
        var dbDownload = await context.DownloadStatuses.FindAsync(download.Id);
        Assert.NotNull(dbDownload);
        Assert.Equal(download.Url, dbDownload.Url);
    }

    [Fact]
    public async Task AddOrUpdateDownload_UpdatesExistingDownload()
    {
        // Arrange
        var download = new DownloadStatus
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file.zip",
            Filename = "file.zip",
            DestinationPath = "/downloads/file.zip",
            Domain = "example.com",
            State = DownloadState.Queued,
            CreatedAt = TestClock.UtcNow,
            LastUpdated = TestClock.UtcNow
        };

        await using (var context = new ServerDbContext(_contextOptions))
        {
            context.DownloadStatuses.Add(download);
            await context.SaveChangesAsync();
        }

        var updatedDownload = new DownloadStatus
        {
            Id = download.Id,
            Url = download.Url,
            Filename = download.Filename,
            DestinationPath = download.DestinationPath,
            Domain = download.Domain,
            State = DownloadState.InProgress,
            CreatedAt = download.CreatedAt,
            LastUpdated = TestClock.UtcNow,
            StartedAt = TestClock.UtcNow
        };

        // Act
        await _service.AddOrUpdateDownloadAsync(updatedDownload);

        // Assert
        var result = _service.GetDownloadById(download.Id);
        Assert.NotNull(result);
        Assert.Equal(DownloadState.InProgress, result.State);

        // Verify it was updated in the database
        await using (var context = new ServerDbContext(_contextOptions))
        {
            var dbDownload = await context.DownloadStatuses.FindAsync(download.Id);
            Assert.NotNull(dbDownload);
            Assert.Equal(DownloadState.InProgress, dbDownload.State);
        }
    }

    [Fact]
    public async Task QueueDownloadAsync_AddsStatusAndQueueRowInOneOperation()
    {
        var download = new DownloadStatus
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file.zip",
            Filename = "file.zip",
            DestinationPath = "/downloads/file.zip",
            DisplayName = "Example file",
            Priority = 10,
            Domain = "example.com",
            SourceType = "Test",
            SourceId = "test-1",
            State = DownloadState.Queued,
            CreatedAt = TestClock.UtcNow,
            LastUpdated = TestClock.UtcNow
        };

        await _service.QueueDownloadAsync(download);

        await using var context = new ServerDbContext(_contextOptions);
        var savedStatus = await context.DownloadStatuses.FindAsync(download.Id);
        var savedQueueRow = await context.QueuedDownloads.FindAsync(download.Id);

        Assert.NotNull(savedStatus);
        Assert.NotNull(savedQueueRow);
        Assert.Equal(DownloadState.Queued, savedStatus.State);
        Assert.Equal(download.Url, savedQueueRow.Url);
        Assert.Equal(download.DestinationPath, savedQueueRow.DestinationPath);
        Assert.Equal(download.DisplayName, savedQueueRow.DisplayName);
        Assert.Equal(download.Priority, savedQueueRow.Priority);
        Assert.Equal(download.SourceType, savedQueueRow.SourceType);
        Assert.Equal(download.SourceId, savedQueueRow.SourceId);
    }

    [Fact]
    public async Task TryRequeuePausedDownloadAsync_WhenPaused_UpdatesStatusAndQueueRow()
    {
        var download = new DownloadStatus
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file.zip",
            Filename = "file.zip",
            DestinationPath = "/downloads/file.zip",
            Domain = "example.com",
            State = DownloadState.Paused,
            CreatedAt = TestClock.UtcNow,
            LastUpdated = TestClock.UtcNow
        };

        await _service.AddOrUpdateDownloadAsync(download);

        var queued = await _service.TryRequeuePausedDownloadAsync(download.Id);

        Assert.True(queued);

        await using var context = new ServerDbContext(_contextOptions);
        var savedStatus = await context.DownloadStatuses.FindAsync(download.Id);
        var savedQueueRow = await context.QueuedDownloads.FindAsync(download.Id);

        Assert.NotNull(savedStatus);
        Assert.NotNull(savedQueueRow);
        Assert.Equal(DownloadState.Queued, savedStatus.State);
        Assert.Equal(download.Id, savedQueueRow.Id);
    }

    [Fact]
    public async Task TryRequeuePausedDownloadAsync_WhenNotPaused_DoesNotQueue()
    {
        var download = new DownloadStatus
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file.zip",
            Filename = "file.zip",
            DestinationPath = "/downloads/file.zip",
            Domain = "example.com",
            State = DownloadState.Failed,
            CreatedAt = TestClock.UtcNow,
            LastUpdated = TestClock.UtcNow
        };

        await _service.AddOrUpdateDownloadAsync(download);

        var queued = await _service.TryRequeuePausedDownloadAsync(download.Id);

        Assert.False(queued);

        await using var context = new ServerDbContext(_contextOptions);
        var savedStatus = await context.DownloadStatuses.FindAsync(download.Id);
        var savedQueueRow = await context.QueuedDownloads.FindAsync(download.Id);

        Assert.NotNull(savedStatus);
        Assert.Null(savedQueueRow);
        Assert.Equal(DownloadState.Failed, savedStatus.State);
    }

    [Fact]
    public async Task TryRequeueFailedOrCanceledDownloadAsync_WhenFailed_ResetsStatusAndQueueRow()
    {
        var download = new DownloadStatus
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file.zip",
            Filename = "file.zip",
            DestinationPath = "/downloads/file.zip",
            Domain = "example.com",
            State = DownloadState.Failed,
            BytesDownloaded = 1024,
            CurrentSpeed = 100,
            ErrorMessage = "Connection error",
            StartedAt = TestClock.UtcNow,
            CompletedAt = TestClock.UtcNow,
            CreatedAt = TestClock.UtcNow,
            LastUpdated = TestClock.UtcNow
        };

        await _service.AddOrUpdateDownloadAsync(download);

        var queued = await _service.TryRequeueFailedOrCanceledDownloadAsync(download.Id);

        Assert.True(queued);

        await using var context = new ServerDbContext(_contextOptions);
        var savedStatus = await context.DownloadStatuses.FindAsync(download.Id);
        var savedQueueRow = await context.QueuedDownloads.FindAsync(download.Id);

        Assert.NotNull(savedStatus);
        Assert.NotNull(savedQueueRow);
        Assert.Equal(DownloadState.Queued, savedStatus.State);
        Assert.Equal(0, savedStatus.BytesDownloaded);
        Assert.Equal(0, savedStatus.CurrentSpeed);
        Assert.Null(savedStatus.ErrorMessage);
        Assert.Null(savedStatus.StartedAt);
        Assert.Null(savedStatus.CompletedAt);
        Assert.Equal(download.Id, savedQueueRow.Id);
    }

    [Fact]
    public async Task TryRequeueFailedOrCanceledDownloadAsync_WhenCanceledOnlyExistsInDatabase_QueuesDownload()
    {
        var download = new DownloadStatus
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file.zip",
            Filename = "file.zip",
            DestinationPath = "/downloads/file.zip",
            Domain = "example.com",
            State = DownloadState.Canceled,
            CreatedAt = TestClock.UtcNow,
            LastUpdated = TestClock.UtcNow
        };

        await using (var context = new ServerDbContext(_contextOptions))
        {
            context.DownloadStatuses.Add(download);
            await context.SaveChangesAsync();
        }

        var queued = await _service.TryRequeueFailedOrCanceledDownloadAsync(download.Id);

        Assert.True(queued);

        await using var verificationContext = new ServerDbContext(_contextOptions);
        var savedStatus = await verificationContext.DownloadStatuses.FindAsync(download.Id);
        var savedQueueRow = await verificationContext.QueuedDownloads.FindAsync(download.Id);

        Assert.NotNull(savedStatus);
        Assert.NotNull(savedQueueRow);
        Assert.Equal(DownloadState.Queued, savedStatus.State);
        Assert.Equal(DownloadState.Queued, _service.GetDownloadById(download.Id)?.State);
    }

    [Fact]
    public async Task RemoveDownload_RemovesDownloadFromServiceAndDatabase()
    {
        // Arrange
        var download = new DownloadStatus
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file.zip",
            Filename = "file.zip",
            DestinationPath = "/downloads/file.zip",
            Domain = "example.com",
            State = DownloadState.Queued,
            CreatedAt = TestClock.UtcNow,
            LastUpdated = TestClock.UtcNow
        };

        await using (var context = new ServerDbContext(_contextOptions))
        {
            context.DownloadStatuses.Add(download);
            await context.SaveChangesAsync();
        }

        await _service.AddOrUpdateDownloadAsync(download);

        var downloadsChanged = 0;
        _service.DownloadsChanged += _ =>
        {
            downloadsChanged++;
            return ValueTask.CompletedTask;
        };

        // Act
        await _service.RemoveDownloadAsync(download.Id);

        // Assert
        var result = _service.GetDownloadById(download.Id);

        Assert.Null(result);
        Assert.Equal(1, downloadsChanged);

        // Verify it was removed from the database
        await using (var context = new ServerDbContext(_contextOptions))
        {
            var dbDownload = await context.DownloadStatuses.FindAsync(download.Id);
            Assert.Null(dbDownload);
        }
    }

    public void Dispose() => _connection.Dispose();

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
}
