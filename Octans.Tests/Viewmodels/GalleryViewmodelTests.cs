using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Octans.Client.Components.Pages;
using Octans.Core.Models;
using Octans.Core.Querying;
using Xunit;

namespace Octans.Tests.Viewmodels;

// AI: Tests focus on observable behaviour of GalleryViewmodel and use NSubstitute for mocking.
public class GalleryViewmodelTests
{
    [Fact]
    public async Task OnQueryChanged_populates_urls_from_hashes_and_sets_progress_to_100()
    {
        // AI: Arrange a query service that returns two known hashes.
        var service = Substitute.For<IQueryService>();
        var hashes = new[]
        {
            new HashItem { Id = 1, Hash = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF } },
            new HashItem { Id = 2, Hash = new byte[] { 0x01, 0x23, 0x45, 0x67 } }
        };

        service.CountAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(hashes.Length);

        service.Query(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(_ => ReturnAsync(hashes));

        var sut = new GalleryViewmodel(service, NullLogger<GalleryViewmodel>.Instance);

        var args = new List<QueryParameter>();

        await sut.OnQueryChanged(args);

        Assert.Equal(new[] { "/media/deadbeef", "/media/01234567" }, sut.ImageUrls);
        Assert.Equal(100, sut.ProgressPercent);
    }

    [Fact]
    public async Task OnQueryChanged_toggles_Searching_true_then_false()
    {
        // AI: Arrange a short run so there are only two state change notifications.
        var service = Substitute.For<IQueryService>();
        var hashes = new[]
        {
            new HashItem { Id = 1, Hash = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD } },
            new HashItem { Id = 2, Hash = new byte[] { 0x10, 0x20, 0x30, 0x40 } }
        };

        service.CountAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(hashes.Length);

        service.Query(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(_ => ReturnAsync(hashes));

        var sut = new GalleryViewmodel(service, NullLogger<GalleryViewmodel>.Instance);

        var firstSearching = (bool?)null;
        var lastSearching = (bool?)null;

        sut.StateChanged = () =>
        {
            if (firstSearching is null)
            {
                firstSearching = sut.Searching;
            }
            else
            {
                lastSearching = sut.Searching;
            }

            return Task.CompletedTask;
        };

        var args = new List<QueryParameter>();

        await sut.OnQueryChanged(args);

        Assert.True(firstSearching is true);
        Assert.True(lastSearching is false);
    }

    [Fact]
    public async Task OnCancel_stops_before_finishing()
    {
        // AI: Arrange a long-running stream and cancel soon after starting.
        var service = Substitute.For<IQueryService>();
        var total = 100;

        service.CountAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(total);

        service.Query(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => SlowStream(total, ci.Arg<CancellationToken>()));

        var sut = new GalleryViewmodel(service, NullLogger<GalleryViewmodel>.Instance);

        var args = new List<QueryParameter>();

        var run = sut.OnQueryChanged(args);

        await Task.Delay(50);

        await sut.OnCancel();

        await run;

        Assert.True(sut.ImageUrls.Count < total);
        Assert.True(sut.ProgressPercent < 100);
    }

    [Fact]
    public async Task Exception_sets_LastError_and_stops_searching()
    {
        // AI: Arrange a service that throws on CountAsync.
        var service = Substitute.For<IQueryService>();

        service.CountAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new InvalidOperationException("boom"));

        var sut = new GalleryViewmodel(service, NullLogger<GalleryViewmodel>.Instance);

        var args = new List<QueryParameter>();

        await sut.OnQueryChanged(args);

        Assert.Equal("boom", sut.LastError);
        Assert.False(sut.Searching);
    }

    // AI: Helper to asynchronously yield provided items.
    private static async IAsyncEnumerable<HashItem> ReturnAsync(IEnumerable<HashItem> items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }

    // AI: Helper to emit a sequence slowly and observe cancellation.
    private static async IAsyncEnumerable<HashItem> SlowStream(
        int count,
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

            yield return new HashItem { Id = i, Hash = hash };
        }
    }
}
