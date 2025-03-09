using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Octans.Core.Downloads;
using Octans.Core.Downloaders;
using Octans.Core.Models;
using System.Data.Common;

namespace Octans.Tests.Downloads;

public class DownloadStateServiceTests : IDisposable
{
    private readonly DbConnection _connection;
    private readonly DbContextOptions<ServerDbContext> _contextOptions;
    private readonly IDbContextFactory<ServerDbContext> _contextFactory;
    private readonly ILogger<DownloadStateService> _logger;
    private readonly DownloadStateService _service;
    private bool _disposedValue;

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
        _contextFactory = Substitute.For<IDbContextFactory<ServerDbContext>>();
        _contextFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new ServerDbContext(_contextOptions)));

        // Setup logger
        _logger = Substitute.For<ILogger<DownloadStateService>>();

        // Create the service
        _service = new DownloadStateService(_logger, _contextFactory);
    }

    [Fact]
    public async Task InitializeFromDbAsync_LoadsActiveDownloads()
    {
        // Arrange
        var activeDownload = new DownloadStatus
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file.zip",
            Filename = "file.zip",
            State = DownloadState.Queued,
            CreatedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
        };

        var completedDownload = new DownloadStatus
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/completed.zip",
            Filename = "completed.zip",
            State = DownloadState.Completed,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            LastUpdated = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        };

        using (var context = new ServerDbContext(_contextOptions))
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
    public void GetDownloadById_ReturnsCorrectDownload()
    {
        // Arrange
        var download = new DownloadStatus
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file.zip",
            Filename = "file.zip",
            State = DownloadState.Queued,
            CreatedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
        };

        _service.AddOrUpdateDownload(download);

        // Act
        var result = _service.GetDownloadById(download.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(download.Id, result.Id);
        Assert.Equal(download.Url, result.Url);
    }

    [Fact]
    public void GetDownloadById_ReturnsNull_WhenIdNotFound()
    {
        // Act
        var result = _service.GetDownloadById(Guid.NewGuid());

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void UpdateProgress_UpdatesDownloadStatus()
    {
        // Arrange
        var download = new DownloadStatus
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file.zip",
            Filename = "file.zip",
            State = DownloadState.InProgress,
            CreatedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow,
            BytesDownloaded = 0,
            TotalBytes = 1000,
            CurrentSpeed = 0
        };

        _service.AddOrUpdateDownload(download);

        bool eventRaised = false;
        _service.OnDownloadProgressChanged += (sender, args) => 
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
    public void UpdateState_UpdatesStateAndRaisesEvents()
    {
        // Arrange
        var download = new DownloadStatus
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file.zip",
            Filename = "file.zip",
            State = DownloadState.Queued,
            CreatedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
        };

        _service.AddOrUpdateDownload(download);

        bool progressEventRaised = false;
        bool downloadsChangedEventRaised = false;

        _service.OnDownloadProgressChanged += (sender, args) => 
        {
            progressEventRaised = true;
            Assert.Equal(DownloadState.InProgress, args.Status.State);
        };

        _service.OnDownloadsChanged += (sender, args) => 
        {
            downloadsChangedEventRaised = true;
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
    }

    [Fact]
    public void UpdateState_SetsCompletedAt_WhenStateIsCompleted()
    {
        // Arrange
        var download = new DownloadStatus
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file.zip",
            Filename = "file.zip",
            State = DownloadState.InProgress,
            CreatedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow
        };

        _service.AddOrUpdateDownload(download);

        // Act
        _service.UpdateState(download.Id, DownloadState.Completed);

        // Assert
        var updated = _service.GetDownloadById(download.Id);
        Assert.NotNull(updated);
        Assert.Equal(DownloadState.Completed, updated.State);
        Assert.NotNull(updated.CompletedAt);
    }

    [Fact]
    public void UpdateState_SetsErrorMessage_WhenStateIsFailed()
    {
        // Arrange
        var download = new DownloadStatus
        {
            Id = Guid.NewGuid(),
            Url = "https://example.com/file.zip",
            Filename = "file.zip",
            State = DownloadState.InProgress,
            CreatedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
        };

        _service.AddOrUpdateDownload(download);

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
            State = DownloadState.Queued,
            CreatedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
        };

        bool eventRaised = false;
        _service.OnDownloadsChanged += (sender, args) => eventRaised = true;

        // Act
        _service.AddOrUpdateDownload(download);

        // Assert
        var result = _service.GetDownloadById(download.Id);
        Assert.NotNull(result);
        Assert.Equal(download.Id, result.Id);
        Assert.True(eventRaised);

        // Verify it was added to the database
        using (var context = new ServerDbContext(_contextOptions))
        {
            var dbDownload = await context.DownloadStatuses.FindAsync(download.Id);
            Assert.NotNull(dbDownload);
            Assert.Equal(download.Url, dbDownload.Url);
        }
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
            State = DownloadState.Queued,
            CreatedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
        };

        using (var context = new ServerDbContext(_contextOptions))
        {
            context.DownloadStatuses.Add(download);
            await context.SaveChangesAsync();
        }

        var updatedDownload = new DownloadStatus
        {
            Id = download.Id,
            Url = download.Url,
            Filename = download.Filename,
            State = DownloadState.InProgress,
            CreatedAt = download.CreatedAt,
            LastUpdated = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow
        };

        // Act
        _service.AddOrUpdateDownload(updatedDownload);

        // Assert
        var result = _service.GetDownloadById(download.Id);
        Assert.NotNull(result);
        Assert.Equal(DownloadState.InProgress, result.State);

        // Verify it was updated in the database
        using (var context = new ServerDbContext(_contextOptions))
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
            State = DownloadState.Queued,
            CreatedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
        };

        using (var context = new ServerDbContext(_contextOptions))
        {
            context.DownloadStatuses.Add(download);
            await context.SaveChangesAsync();
        }

        _service.AddOrUpdateDownload(download);

        bool eventRaised = false;
        _service.OnDownloadsChanged += (sender, args) => eventRaised = true;

        // Act
        _service.RemoveDownload(download.Id);

        // Assert
        var result = _service.GetDownloadById(download.Id);
        Assert.Null(result);
        Assert.True(eventRaised);

        // Verify it was removed from the database
        using (var context = new ServerDbContext(_contextOptions))
        {
            var dbDownload = await context.DownloadStatuses.FindAsync(download.Id);
            Assert.Null(dbDownload);
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _connection.Dispose();
            }
            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
