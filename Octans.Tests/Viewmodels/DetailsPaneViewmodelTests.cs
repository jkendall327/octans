using MudBlazor;
using NSubstitute;
using Octans.Client;
using Octans.Client.Components.Gallery;
using Octans.Core.Notes;
using Octans.Core.Repositories;
using Octans.Core.Tags;
using Octans.Data.Models;
using Octans.Tests.Helpers;

namespace Octans.Tests.Viewmodels;

public sealed class DetailsPaneViewmodelTests
{
    private const string Hash = "DEADBEEF";

    private readonly IOctansClient _client = Substitute.For<IOctansClient>();
    private readonly ISnackbar _snackbar = Substitute.For<ISnackbar>();
    private readonly DetailsPaneViewmodel _sut;

    public DetailsPaneViewmodelTests()
    {
        _client.GetMediaDetailsAsync(Arg.Any<string>()).Returns(EmptyDetails(Hash));
        _sut = new(_snackbar, _client);
    }

    [Fact]
    public async Task SelectHashAsync_loads_notes_and_tags()
    {
        var note = new NoteDto(1, "hello", TestClock.UtcNow, TestClock.UtcNow);
        _client.GetMediaDetailsAsync(Hash).Returns(Details(Hash, [note], [new("artist", "someone")]));

        await _sut.SelectHashAsync(Hash);

        Assert.Equal(Hash, _sut.SelectedHash);
        Assert.True(_sut.HasSelection);
        Assert.Equal([note], _sut.Notes);
        var tag = Assert.Single(_sut.Tags);
        Assert.Equal("artist", tag.Namespace);
        Assert.Equal("someone", tag.Subtag);
    }

    [Fact]
    public async Task SelectHashAsync_clears_state_when_selection_is_empty()
    {
        _client.GetMediaDetailsAsync(Hash).Returns(Details(
            Hash,
            [new NoteDto(1, "hello", TestClock.UtcNow, TestClock.UtcNow)],
            [new("artist", "someone")]));
        await _sut.SelectHashAsync(Hash);
        _sut.NewNoteContent = "draft";

        await _sut.SelectHashAsync(null);

        Assert.Null(_sut.SelectedHash);
        Assert.False(_sut.HasSelection);
        Assert.Empty(_sut.Notes);
        Assert.Empty(_sut.Tags);
        Assert.Empty(_sut.NewNoteContent);
    }

    [Fact]
    public async Task ArchiveImage_queues_repository_change_for_selected_hash()
    {
        await _sut.SelectHashAsync(Hash);

        await _sut.ArchiveImage();

        await _client
            .Received(1)
            .TransitionRepositoryItemsAsync(
                Arg.Is<IEnumerable<string>>(hashes => hashes.SequenceEqual(new[] { Hash })),
                RepositoryDestination.Archive);
    }

    [Fact]
    public async Task AddNote_appends_note_and_clears_input()
    {
        var note = new NoteDto(3, "saved", TestClock.UtcNow, TestClock.UtcNow);
        _client.AddNoteAsync(Hash, "saved").Returns(note);
        await _sut.SelectHashAsync(Hash);
        _sut.NewNoteContent = "saved";

        await _sut.AddNote();

        Assert.Equal([note], _sut.Notes);
        Assert.Empty(_sut.NewNoteContent);
    }

    [Fact]
    public async Task DeleteNote_removes_note()
    {
        var note = new NoteDto(3, "saved", TestClock.UtcNow, TestClock.UtcNow);
        _client.GetMediaDetailsAsync(Hash).Returns(Details(Hash, [note], []));
        await _sut.SelectHashAsync(Hash);

        await _sut.DeleteNote(note);

        Assert.Empty(_sut.Notes);
        await _client.Received(1).DeleteNoteAsync(note.Id);
    }

    [Fact]
    public async Task Mutations_raise_state_changed()
    {
        var note = new NoteDto(3, "saved", TestClock.UtcNow, TestClock.UtcNow);
        _client.AddNoteAsync(Hash, "saved").Returns(note);
        await _sut.SelectHashAsync(Hash);
        _sut.NewNoteContent = "saved";
        var callCount = 0;
        _sut.StateChanged += () =>
        {
            callCount++;
            return Task.CompletedTask;
        };

        await _sut.AddNote();

        Assert.Equal(1, callCount);
    }

    private static MediaDetailsDto EmptyDetails(string hash) => Details(hash, [], []);

    private static MediaDetailsDto Details(
        string hash,
        IReadOnlyList<NoteDto> notes,
        IReadOnlyList<TagModel> tags) =>
        new(1, hash, ".jpg", "image/jpeg", RepositoryType.Inbox, tags, notes, $"/media/{hash}");
}
