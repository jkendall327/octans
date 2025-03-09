using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Octans.Core.Downloads;
using Octans.Core.Downloaders;
using Octans.Core.Models;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Octans.Tests.Downloads;

public sealed class DownloadStateServiceTests : IDisposable, IAsyncDisposable
{
    private readonly DbConnection _connection;
    private readonly DbContextOptions<ServerDbContext> _contextOptions;
    private readonly DownloadStateService _service;

    public DownloadStateServiceTests()
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

        _service = new(NullLogger<DownloadStateService>.Instance, contextFactory);
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
            CreatedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
        };

        var completedDownload = new DownloadStatus
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/completed.zip",
            Filename = "completed.zip",
            DestinationPath = "/downloads/completed.zip",
            Domain = "example.com",
            State = DownloadState.Completed,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            LastUpdated = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
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
            CreatedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
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
            CreatedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow,
            BytesDownloaded = 0,
            TotalBytes = 1000,
            CurrentSpeed = 0
        };

        await _service.AddOrUpdateDownloadAsync(download);

        var eventRaised = false;
        _service.OnDownloadProgressChanged += (_, args) =>
        {
            eventRaised = true;
            Assert.Equal(500, args.Status.BytesDownloaded);
            Assert.Equal(1000, args.Status.TotalBytes);
            Assert.Equal(100.0, args.Status.CurrentSpeed);
        };

        // Act
        _service.UpdateProgress(download.Id, 500, 1000, 100.0);

        // Assert
        var updated = _service.GetDownloadById(download.Id);
        Assert.NotNull(updated);
        Assert.Equal(500, updated.BytesDownloaded);
        Assert.Equal(1000, updated.TotalBytes);
        Assert.Equal(100.0, updated.CurrentSpeed);
        Assert.True(eventRaised);
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
            CreatedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
        };

        await _service.AddOrUpdateDownloadAsync(download);

        var progressEventRaised = false;
        var downloadsChangedEventRaised = false;
        Guid? affectedId = null;
        DownloadChangeType? changeType = null;

        _service.OnDownloadProgressChanged += (_, args) =>
        {
            progressEventRaised = true;
            Assert.Equal(DownloadState.InProgress, args.Status.State);
        };

        _service.OnDownloadsChanged += (_, args) =>
        {
            downloadsChangedEventRaised = true;
            affectedId = args.AffectedDownloadId;
            changeType = args.ChangeType;
        };

        // Act
        _service.UpdateState(download.Id, DownloadState.InProgress);

        // Assert
        var updated = _service.GetDownloadById(download.Id);
        Assert.NotNull(updated);
        Assert.Equal(DownloadState.InProgress, updated.State);
        Assert.NotNull(updated.StartedAt);
        Assert.True(progressEventRaised);
        Assert.True(downloadsChangedEventRaised);
        Assert.Equal(download.Id, affectedId);
        Assert.Equal(DownloadChangeType.Updated, changeType);
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
            CreatedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow
        };

        await _service.AddOrUpdateDownloadAsync(download);

        // Act
        _service.UpdateState(download.Id, DownloadState.Completed);

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
            CreatedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
        };

        await _service.AddOrUpdateDownloadAsync(download);

        // Act
        _service.UpdateState(download.Id, DownloadState.Failed, "Download failed due to network error");

        // Assert
        var updated = _service.GetDownloadById(download.Id);
        Assert.NotNull(updated);
        Assert.Equal(DownloadState.Failed, updated.State);
        Assert.Equal("Download failed due to network error", updated.ErrorMessage);
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
            CreatedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
        };

        var eventRaised = false;
        Guid? affectedId = null;
        DownloadChangeType? changeType = null;

        _service.OnDownloadsChanged += (_, args) =>
        {
            eventRaised = true;
            affectedId = args.AffectedDownloadId;
            changeType = args.ChangeType;
        };

        // Act
        await _service.AddOrUpdateDownloadAsync(download);

        // Assert
        var result = _service.GetDownloadById(download.Id);
        Assert.NotNull(result);
        Assert.Equal(download.Id, result.Id);
        Assert.True(eventRaised);

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
            CreatedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
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
            LastUpdated = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow
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
            CreatedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
        };

        await using (var context = new ServerDbContext(_contextOptions))
        {
            context.DownloadStatuses.Add(download);
            await context.SaveChangesAsync();
        }

        await _service.AddOrUpdateDownloadAsync(download);

        var eventRaised = false;
        Guid? affectedId = null;
        DownloadChangeType? changeType = null;

        _service.OnDownloadsChanged += (_, args) =>
        {
            eventRaised = true;
            affectedId = args.AffectedDownloadId;
            changeType = args.ChangeType;
        };

        // Act
        await _service.RemoveDownloadAsync(download.Id);

        // Assert
        var result = _service.GetDownloadById(download.Id);
        Assert.Null(result);
        Assert.True(eventRaised);

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
