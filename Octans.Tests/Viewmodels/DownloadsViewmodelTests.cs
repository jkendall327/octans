using NSubstitute;
using Octans.Client;
using Octans.Client.Components.Downloads;
using Octans.Core.Http.Models;
using Octans.Data.Models;
using Octans.Tests.Helpers;

namespace Octans.Tests.Viewmodels;

public class DownloadsViewmodelTests
{
    private readonly IOctansClient _client = Substitute.For<IOctansClient>();
    private readonly DownloadsViewmodel _sut;

    public DownloadsViewmodelTests()
    {
        _sut = new(_client);
    }

    [Fact]
    public async Task InitializeAsync_populates_active_downloads()
    {
        var status = CreateStatus();
        _client.GetDownloadsAsync().Returns([status]);

        await _sut.InitializeAsync();

        Assert.Single(_sut.ActiveDownloads);
        Assert.Equal(status.Id, _sut.ActiveDownloads[0].Id);
    }

    [Fact]
    public async Task RefreshAsync_refreshes_and_raises_event()
    {
        var status = CreateStatus();
        _client.GetDownloadsAsync().Returns([status]);

        var triggered = false;
        _sut.StateChanged += () =>
        {
            triggered = true;
            return Task.CompletedTask;
        };

        await _sut.RefreshAsync();

        Assert.True(triggered);
        Assert.Single(_sut.ActiveDownloads);
        Assert.Equal(status.Id, _sut.ActiveDownloads[0].Id);
    }

    private static DownloadStatusDto CreateStatus(Guid? id = null, long bytesDownloaded = 0) => new(
        id ?? Guid.NewGuid(),
        "https://example.com/file.zip",
        "file.zip",
        null,
        "/downloads/file.zip",
        "example.com",
        0,
        100,
        bytesDownloaded,
        bytesDownloaded,
        0,
        DownloadState.InProgress,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        TestClock.UtcNow,
        null,
        null,
        TestClock.UtcNow);
}
