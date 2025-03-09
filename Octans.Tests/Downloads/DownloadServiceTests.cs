using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Octans.Core.Downloads;
using Octans.Core.Downloaders;
using Xunit;

namespace Octans.Tests.Downloads;

public class DownloadServiceTests
{
    private readonly Mock<IDownloadQueue> _mockQueue = new();
    private readonly Mock<IDownloadStateService> _mockStateService = new();
    private readonly ILogger<DownloadService> _logger = NullLogger<DownloadService>.Instance;
    private readonly DownloadService _service;

    public DownloadServiceTests()
    {
        _service = new DownloadService(_mockQueue, _mockStateService, _logger);
    }

    [Fact]
    public async Task QueueDownloadAsync_ShouldCreateNewDownload()
    {
        // Arrange
        var request = new DownloadRequest
        {
            Url = new Uri("https://example.com/file.zip"),
            DestinationPath = "/downloads/file.zip",
            Priority = 1
        };

        // Act
        var id = await _service.QueueDownloadAsync(request);

        // Assert
        Assert.NotEqual(Guid.Empty, id);
        
        _mockStateService.Verify(s => s.AddOrUpdateDownloadAsync(
            It.Is<DownloadStatus>(ds => 
                ds.Id == id && 
                ds.Url == request.Url.ToString() &&
                ds.DestinationPath == request.DestinationPath &&
                ds.State == DownloadState.Queued &&
                ds.Domain == "example.com")), 
            Times.Once);
        
        _mockQueue.Verify(q => q.EnqueueAsync(
            It.Is<QueuedDownload>(qd => 
                qd.Id == id && 
                qd.Url == request.Url.ToString() &&
                qd.DestinationPath == request.DestinationPath &&
                qd.Priority == request.Priority &&
                qd.Domain == "example.com")), 
            Times.Once);
    }

    [Fact]
    public async Task CancelDownloadAsync_ShouldCancelAndUpdateState()
    {
        // Arrange
        var id = Guid.NewGuid();
        
        // Act
        await _service.CancelDownloadAsync(id);
        
        // Assert
        _mockQueue.Verify(q => q.RemoveAsync(id), Times.Once);
        _mockStateService.Verify(s => s.UpdateState(id, DownloadState.Canceled), Times.Once);
    }

    [Fact]
    public async Task PauseDownloadAsync_ShouldUpdateState()
    {
        // Arrange
        var id = Guid.NewGuid();
        
        // Act
        await _service.PauseDownloadAsync(id);
        
        // Assert
        _mockStateService.Verify(s => s.UpdateState(id, DownloadState.Paused), Times.Once);
    }

    [Fact]
    public async Task ResumeDownloadAsync_WhenPaused_ShouldRequeueAndUpdateState()
    {
        // Arrange
        var id = Guid.NewGuid();
        var status = new DownloadStatus
        {
            Id = id,
            Url = "https://example.com/file.zip",
            DestinationPath = "/downloads/file.zip",
            State = DownloadState.Paused,
            Domain = "example.com"
        };
        
        _mockStateService.Setup(s => s.GetDownloadById(id)).Returns(status);
        
        // Act
        await _service.ResumeDownloadAsync(id);
        
        // Assert
        _mockQueue.Verify(q => q.EnqueueAsync(
            It.Is<QueuedDownload>(qd => 
                qd.Id == id && 
                qd.Url == status.Url &&
                qd.DestinationPath == status.DestinationPath)), 
            Times.Once);
        
        _mockStateService.Verify(s => s.UpdateState(id, DownloadState.Queued), Times.Once);
    }

    [Fact]
    public async Task ResumeDownloadAsync_WhenNotPaused_ShouldNotRequeue()
    {
        // Arrange
        var id = Guid.NewGuid();
        var status = new DownloadStatus
        {
            Id = id,
            Url = "https://example.com/file.zip",
            DestinationPath = "/downloads/file.zip",
            State = DownloadState.Failed,
            Domain = "example.com"
        };
        
        _mockStateService.Setup(s => s.GetDownloadById(id)).Returns(status);
        
        // Act
        await _service.ResumeDownloadAsync(id);
        
        // Assert
        _mockQueue.Verify(q => q.EnqueueAsync(It.IsAny<QueuedDownload>()), Times.Never);
        _mockStateService.Verify(s => s.UpdateState(id, It.IsAny<DownloadState>()), Times.Never);
    }

    [Fact]
    public async Task RetryDownloadAsync_WhenFailed_ShouldResetAndRequeue()
    {
        // Arrange
        var id = Guid.NewGuid();
        var status = new DownloadStatus
        {
            Id = id,
            Url = "https://example.com/file.zip",
            DestinationPath = "/downloads/file.zip",
            State = DownloadState.Failed,
            BytesDownloaded = 1024,
            CurrentSpeed = 100,
            ErrorMessage = "Connection error",
            Domain = "example.com"
        };
        
        _mockStateService.Setup(s => s.GetDownloadById(id)).Returns(status);
        
        // Act
        await _service.RetryDownloadAsync(id);
        
        // Assert
        Assert.Equal(0, status.BytesDownloaded);
        Assert.Equal(0, status.CurrentSpeed);
        Assert.Null(status.ErrorMessage);
        Assert.Null(status.StartedAt);
        Assert.Null(status.CompletedAt);
        
        _mockQueue.Verify(q => q.EnqueueAsync(
            It.Is<QueuedDownload>(qd => 
                qd.Id == id && 
                qd.Url == status.Url &&
                qd.DestinationPath == status.DestinationPath)), 
            Times.Once);
        
        _mockStateService.Verify(s => s.UpdateState(id, DownloadState.Queued), Times.Once);
    }

    [Fact]
    public async Task RetryDownloadAsync_WhenCanceled_ShouldResetAndRequeue()
    {
        // Arrange
        var id = Guid.NewGuid();
        var status = new DownloadStatus
        {
            Id = id,
            Url = "https://example.com/file.zip",
            DestinationPath = "/downloads/file.zip",
            State = DownloadState.Canceled,
            Domain = "example.com"
        };
        
        _mockStateService.Setup(s => s.GetDownloadById(id)).Returns(status);
        
        // Act
        await _service.RetryDownloadAsync(id);
        
        // Assert
        _mockQueue.Verify(q => q.EnqueueAsync(It.IsAny<QueuedDownload>()), Times.Once);
        _mockStateService.Verify(s => s.UpdateState(id, DownloadState.Queued), Times.Once);
    }

    [Fact]
    public async Task RetryDownloadAsync_WhenNotFailedOrCanceled_ShouldNotRequeue()
    {
        // Arrange
        var id = Guid.NewGuid();
        var status = new DownloadStatus
        {
            Id = id,
            Url = "https://example.com/file.zip",
            DestinationPath = "/downloads/file.zip",
            State = DownloadState.InProgress,
            Domain = "example.com"
        };
        
        _mockStateService.Setup(s => s.GetDownloadById(id)).Returns(status);
        
        // Act
        await _service.RetryDownloadAsync(id);
        
        // Assert
        _mockQueue.Verify(q => q.EnqueueAsync(It.IsAny<QueuedDownload>()), Times.Never);
        _mockStateService.Verify(s => s.UpdateState(id, It.IsAny<DownloadState>()), Times.Never);
    }

    [Fact]
    public void GetDownloadToken_ShouldReturnCancellationToken()
    {
        // Arrange
        var id = Guid.NewGuid();
        
        // Act
        var token = _service.GetDownloadToken(id);
        
        // Assert
        Assert.NotNull(token);
        Assert.False(token.IsCancellationRequested);
    }

    [Fact]
    public async Task CancelDownloadAsync_ShouldCancelToken()
    {
        // Arrange
        var id = Guid.NewGuid();
        var token = _service.GetDownloadToken(id);
        
        // Act
        await _service.CancelDownloadAsync(id);
        
        // Assert
        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        // Act & Assert
        var exception = Record.Exception(() => _service.Dispose());
        Assert.Null(exception);
    }
}
