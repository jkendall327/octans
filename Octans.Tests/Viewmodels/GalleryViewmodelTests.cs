using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Octans.Client;
using Octans.Client.Components.Pages;
using Octans.Client.Components.StatusBar;
using Octans.Core.Models;
using Octans.Core.Querying;

namespace Octans.Tests.Viewmodels;

public class GalleryViewmodelTests
{
    private readonly IQueryService _service;
    private readonly GalleryViewmodel _sut;
    private readonly IBrowserStorage _storage = Substitute.For<IBrowserStorage>();

    private static readonly string[] Expected =
    [
        "/media/deadbeef", "/media/01234567"
    ];

    public GalleryViewmodelTests()
    {
        _service = Substitute.For<IQueryService>();
        _sut = new(_service, _storage, new(), NullLogger<GalleryViewmodel>.Instance);
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