using NSubstitute;
using Octans.Client.Downloads;
using Octans.Core.Downloaders;
using Octans.Core.Downloads;

namespace Octans.Tests.Viewmodels;

public class DownloadsViewmodelTests
{
    private readonly IDownloadStateService _stateService = Substitute.For<IDownloadStateService>();
    private readonly DownloadsViewmodel _sut;

    public DownloadsViewmodelTests()
    {
        _sut = new(_stateService);
    }

    [Fact]
    public async Task InitializeAsync_populates_active_downloads()
    {
        var status = CreateStatus();
        _stateService.GetAllDownloads().Returns(new List<DownloadStatus> { status });

        await _sut.InitializeAsync();

        Assert.Single(_sut.ActiveDownloads);
        Assert.Equal(status.Id, _sut.ActiveDownloads[0].Id);
    }

    [Fact]
    public async Task HandleDownloadsChanged_refreshes_and_raises_event()
    {
        var status = CreateStatus();
        _stateService.GetAllDownloads().Returns(new List<DownloadStatus> { status });

        var triggered = false;
        _sut.StateChanged += () =>
        {
            triggered = true;
            return Task.CompletedTask;
        };

        await _sut.Handle(new DownloadsChanged { ChangeType = DownloadChangeType.Added }, CancellationToken.None);

        Assert.True(triggered);
        Assert.Single(_sut.ActiveDownloads);
        Assert.Equal(status.Id, _sut.ActiveDownloads[0].Id);
    }

    [Fact]
    public async Task HandleDownloadStatusChanged_updates_existing_and_raises_event()
    {
        var id = Guid.NewGuid();
        var existing = CreateStatus(id, bytesDownloaded: 10);
        _sut.ActiveDownloads.Add(existing);

        var updated = CreateStatus(id, bytesDownloaded: 50);

        var triggered = false;
        _sut.StateChanged += () =>
        {
            triggered = true;
            return Task.CompletedTask;
        };

        await _sut.Handle(new DownloadStatusChanged { Status = updated }, CancellationToken.None);

        Assert.True(triggered);
        Assert.Single(_sut.ActiveDownloads);
        Assert.Equal(50, _sut.ActiveDownloads[0].BytesDownloaded);
    }

    [Fact]
    public async Task HandleDownloadStatusChanged_adds_new_download_and_raises_event()
    {
        var status = CreateStatus();
        var triggered = false;
        _sut.StateChanged += () =>
        {
            triggered = true;
            return Task.CompletedTask;
        };

        await _sut.Handle(new DownloadStatusChanged { Status = status }, CancellationToken.None);

        Assert.True(triggered);
        Assert.Single(_sut.ActiveDownloads);
        Assert.Equal(status.Id, _sut.ActiveDownloads[0].Id);
    }

    private static DownloadStatus CreateStatus(Guid? id = null, long bytesDownloaded = 0)
    {
        return new DownloadStatus
        {
            Id = id ?? Guid.NewGuid(),
            Url = "https://example.com/file.zip",
            Filename = "file.zip",
            DestinationPath = "/downloads/file.zip",
            Domain = "example.com",
            TotalBytes = 100,
            BytesDownloaded = bytesDownloaded,
            State = DownloadState.InProgress,
            CreatedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
        };
    }
}
