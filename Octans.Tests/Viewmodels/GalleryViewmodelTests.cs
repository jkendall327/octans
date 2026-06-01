using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Octans.Client;
using Octans.Client.Components.Gallery;
using Octans.Client.Components.StatusBar;
using Octans.Client.Services;
using Octans.Core.Querying;
using Octans.Core.Repositories;
using Octans.Core.Scripting;
using Octans.Data.Models;

namespace Octans.Tests.Viewmodels;

public class GalleryViewmodelTests
{
    private readonly IOctansClient _client;
    private readonly GalleryViewmodel _sut;
    private readonly IBrowserStorage _storage = Substitute.For<IBrowserStorage>();
    private readonly IClipboardService _clipboard = Substitute.For<IClipboardService>();
    private readonly ICustomCommandProvider _commandProvider = Substitute.For<ICustomCommandProvider>();
    private readonly IBrowserService _browserService = Substitute.For<IBrowserService>();
    private readonly StatusService _status = new();

    private static readonly string[] Expected =
    [
        "/media/DEADBEEF", "/media/01234567"
    ];

    private static readonly string[] DeadbeefHash = ["DEADBEEF"];
    private static readonly string[] NumericHash = ["01234567"];

    public GalleryViewmodelTests()
    {
        _client = Substitute.For<IOctansClient>();

        _sut = new(_client,
            _storage,
            _clipboard,
            _status,
            _commandProvider,
            _browserService,
            NullLogger<GalleryViewmodel>.Instance);
    }

    [Fact]
    public async Task OnQueryChanged_populates_urls_from_hashes_and_sets_progress_to_100()
    {
        var hashes = new[]
        {
            new FileDto(1, "DEADBEEF", null, null, RepositoryType.Inbox, "/media/DEADBEEF"),
            new FileDto(2, "01234567", null, null, RepositoryType.Inbox, "/media/01234567")
        };

        _client
            .CountQueryFilesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(hashes.Length);

        _client
            .QueryFilesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(hashes);

        var args = new List<QueryParameter>();

        await _sut.OnQueryChanged(args);

        Assert.Equal(Expected, _sut.ImageUrls);

        Assert.Equal(100, _sut.ProgressPercent);
    }

    [Fact]
    public async Task OnCancel_stops_before_finishing()
    {
        var total = 100;

        _client
            .CountQueryFilesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(total);

        _client
            .QueryFilesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ci.Arg<CancellationToken>());
                return [];
            });

        var args = new List<QueryParameter>();

        var run = _sut.OnQueryChanged(args);

        await Task.Delay(50);

        await _sut.OnCancel();

        await run;

        Assert.True(_sut.ImageUrls.Count < total);
        Assert.True(_sut.ProgressPercent < 100);
    }

    [Fact]
    public async Task Exception_sets_LastError_and_stops_searching()
    {
        _client
            .CountQueryFilesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new InvalidOperationException("boom"));

        var args = new List<QueryParameter>();

        await _sut.OnQueryChanged(args);

        Assert.Equal("boom", _sut.LastError);
        Assert.False(_sut.Searching);
    }

    [Fact]
    public async Task OnFilterComplete_writes_repository_requests()
    {
        var result = new ImageViewerFilterResult
        {
            Choices = new()
            {
                ["/media/DEADBEEF"] = ImageViewerFilterChoice.Archive,
                ["/media/01234567"] = ImageViewerFilterChoice.Delete
            }
        };

        await _sut.OnFilterComplete(result);

        await _client
            .Received(1)
            .TransitionRepositoryItemsAsync(
                Arg.Is<IEnumerable<string>>(hashes => hashes.SequenceEqual(DeadbeefHash)),
                RepositoryDestination.Archive);
        await _client
            .Received(1)
            .TransitionRepositoryItemsAsync(
                Arg.Is<IEnumerable<string>>(hashes => hashes.SequenceEqual(NumericHash)),
                RepositoryDestination.Trash);
    }

    [Fact]
    public async Task OnFilterComplete_filters_out_trashed_images_only()
    {
        _sut.ImageUrls.AddRange(Expected);

        var result = new ImageViewerFilterResult
        {
            Choices = new()
            {
                ["/media/DEADBEEF"] = ImageViewerFilterChoice.Archive,
                ["/media/01234567"] = ImageViewerFilterChoice.Delete
            }
        };

        await _sut.OnFilterComplete(result);

        Assert.Contains("/media/DEADBEEF", _sut.ImageUrls);
        Assert.DoesNotContain("/media/01234567", _sut.ImageUrls);
    }

    [Fact]
    public async Task OnDelete_queues_repository_change_and_removes_from_list()
    {
        _commandProvider
            .GetCustomCommandsAsync()
            .Returns([]);

        await _sut.OnInitialized();

        _sut.ImageUrls.AddRange(Expected);

        var toDelete = new List<string>
        {
            Expected[0]
        };

        var deleteItem = _sut.ContextMenuItems.Single(i => i.Text == "Delete");
        await deleteItem.Action!(toDelete);

        Assert.DoesNotContain(Expected[0], _sut.ImageUrls);

        await _client
            .Received(1)
            .TransitionRepositoryItemsAsync(
                Arg.Is<IEnumerable<string>>(hashes => hashes.SequenceEqual(DeadbeefHash)),
                RepositoryDestination.Trash);
    }

    [Fact]
    public async Task OnCopyUrl_copies_all_urls_joined_by_newlines()
    {
        _commandProvider
            .GetCustomCommandsAsync()
            .Returns([]);

        await _sut.OnInitialized();

        var toCopy = new List<string>(Expected);

        var copyItem = _sut.ContextMenuItems.Single(i => i.Text == "Copy URL");
        await copyItem.Action!(toCopy);

        await _clipboard
            .ReceivedWithAnyArgs(1)
            .CopyToClipboard("/media/DEADBEEF\n/media/01234567");

        Assert.Equal("Copied 2 URL(s)", _status.GenericText);
    }

}
