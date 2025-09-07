using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Octans.Client;
using Octans.Client.Components.Pages;
using Octans.Client.Components.Gallery;
using Octans.Client.Components.StatusBar;
using Octans.Core.Models;
using Octans.Core.Querying;
using Octans.Core.Repositories;
using Octans.Core.Scripting;
using Microsoft.JSInterop;

namespace Octans.Tests.Viewmodels;

public class GalleryViewmodelTests
{
    private readonly IQueryService _service;
    private readonly GalleryViewmodel _sut;
    private readonly IBrowserStorage _storage = Substitute.For<IBrowserStorage>();
    private readonly IClipboard _clipboard = Substitute.For<IClipboard>();
    private readonly SpyChannelWriter<RepositoryChangeRequest> _repoChannel = new();
    private readonly ICustomCommandProvider _commandProvider = Substitute.For<ICustomCommandProvider>();
    private readonly IJSRuntime _js = Substitute.For<IJSRuntime>();
    private readonly StatusService _status = new();

    private static readonly string[] Expected =
    [
        "/media/DEADBEEF", "/media/01234567"
    ];

    public GalleryViewmodelTests()
    {
        _service = Substitute.For<IQueryService>();

        _sut = new(_service,
            _storage,
            _clipboard,
            _status,
            _commandProvider,
            _repoChannel,
            _js,
            NullLogger<GalleryViewmodel>.Instance);
    }

    [Fact]
    public async Task OnQueryChanged_populates_urls_from_hashes_and_sets_progress_to_100()
    {
        var hashes = new[]
        {
            new HashItem
            {
                Id = 1,
                Hash =
                [
                    0xDE, 0xAD, 0xBE, 0xEF
                ]
            },
            new HashItem
            {
                Id = 2,
                Hash =
                [
                    0x01, 0x23, 0x45, 0x67
                ]
            }
        };

        _service
            .CountAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(hashes.Length);

        _service
            .Query(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(_ => ReturnAsync(hashes));

        var args = new List<QueryParameter>();

        await _sut.OnQueryChanged(args);

        Assert.Equal(Expected, _sut.ImageUrls);

        Assert.Equal(100, _sut.ProgressPercent);
    }

    [Fact]
    public async Task OnCancel_stops_before_finishing()
    {
        var total = 100;

        _service
            .CountAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(total);

        _service
            .Query(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => SlowStream(total, ci.Arg<CancellationToken>()));

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
        _service
            .CountAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new InvalidOperationException("boom"));

        var args = new List<QueryParameter>();

        await _sut.OnQueryChanged(args);

        Assert.Equal("boom", _sut.LastError);
        Assert.False(_sut.Searching);
    }

    [Fact]
    public async Task OnFilterComplete_writes_repository_requests()
    {
        var result = new ImageViewer.FilterResult
        {
            Choices = new()
            {
                ["/media/DEADBEEF"] = ImageViewer.FilterChoice.Archive,
                ["/media/01234567"] = ImageViewer.FilterChoice.Delete
            }
        };

        await _sut.OnFilterComplete(result);

        var items = new List<RepositoryChangeRequest>();

        while (_repoChannel.Channel.Reader.TryRead(out var item))
        {
            items.Add(item);
        }

        Assert.Contains(items, r => r is { Hash: "DEADBEEF", Destination: RepositoryType.Archive });
        Assert.Contains(items, r => r is { Hash: "01234567", Destination: RepositoryType.Trash });
    }

    [Fact]
    public async Task OnFilterComplete_filters_out_trashed_images_only()
    {
        _sut.ImageUrls.AddRange(Expected);

        var result = new ImageViewer.FilterResult
        {
            Choices = new()
            {
                ["/media/DEADBEEF"] = ImageViewer.FilterChoice.Archive,
                ["/media/01234567"] = ImageViewer.FilterChoice.Delete
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
            .Returns(new List<CustomCommand>());

        await _sut.OnInitialized();

        _sut.ImageUrls.AddRange(Expected);

        var toDelete = new List<string>
        {
            Expected[0]
        };

        var deleteItem = _sut.ContextMenuItems.Single(i => i.Text == "Delete");
        await deleteItem.Action!(toDelete);

        Assert.DoesNotContain(Expected[0], _sut.ImageUrls);

        Assert.True(_repoChannel.Channel.Reader.TryRead(out var item));
        Assert.Equal("DEADBEEF", item.Hash);
        Assert.Equal(RepositoryType.Trash, item.Destination);
    }

    [Fact]
    public async Task OnCopyUrl_copies_all_urls_joined_by_newlines()
    {
        _commandProvider
            .GetCustomCommandsAsync()
            .Returns(new List<CustomCommand>());

        await _sut.OnInitialized();

        var toCopy = new List<string>(Expected);

        var copyItem = _sut.ContextMenuItems.Single(i => i.Text == "Copy URL");
        await copyItem.Action!(toCopy);

        await _clipboard
            .ReceivedWithAnyArgs(1)
            .CopyToClipboardAsync("/media/DEADBEEF\n/media/01234567");

        Assert.Equal("Copied 2 URL(s)", _status.GenericText);
    }

    private static async IAsyncEnumerable<HashItem> ReturnAsync(IEnumerable<HashItem> items)
    {
        foreach (var item in items)
        {
            await Task.Yield();

            yield return item;
        }
    }

    private static async IAsyncEnumerable<HashItem> SlowStream(int count,
        [EnumeratorCancellation] CancellationToken token)
    {
        for (var i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();

            await Task.Delay(10, token);

            var hash = BitConverter.GetBytes(i);

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(hash);
            }

            yield return new()
            {
                Id = i,
                Hash = hash
            };
        }
    }
}