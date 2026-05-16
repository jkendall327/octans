using MudBlazor;
using NSubstitute;
using Octans.Client.Components.Gallery;
using Octans.Core.Notes;
using Octans.Core.Repositories;
using Octans.Core.Tags;
using Octans.Tests.Helpers;

namespace Octans.Tests.Viewmodels;

public sealed class DetailsPaneViewmodelTests
{
    private const string Hash = "DEADBEEF";

    private readonly INoteService _noteService = Substitute.For<INoteService>();
    private readonly ITagService _tagService = Substitute.For<ITagService>();
    private readonly ISnackbar _snackbar = Substitute.For<ISnackbar>();
    private readonly SpyChannelWriter<RepositoryChangeRequest> _repositoryChannel = new();
    private readonly DetailsPaneViewmodel _sut;

    public DetailsPaneViewmodelTests()
    {
        _noteService.GetNotesAsync(Arg.Any<string>()).Returns([]);
        _tagService.GetTagsForHashAsync(Arg.Any<string>()).Returns([]);
        _sut = new(_noteService, _tagService, _snackbar, _repositoryChannel);
    }

    [Fact]
    public async Task SelectHashAsync_loads_notes_and_tags()
    {
        var note = new NoteDto(1, "hello", TestClock.UtcNow, TestClock.UtcNow);
        _noteService.GetNotesAsync(Hash).Returns([note]);
        _tagService.GetTagsForHashAsync(Hash).Returns([new("artist", "someone")]);

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
        _noteService.GetNotesAsync(Hash).Returns([new NoteDto(1, "hello", TestClock.UtcNow, TestClock.UtcNow)]);
        _tagService.GetTagsForHashAsync(Hash).Returns([new("artist", "someone")]);
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

        var item = Assert.Single(_repositoryChannel.WrittenItems);
        Assert.Equal(Hash, item.Hash);
        Assert.Equal(RepositoryDestination.Archive, item.Destination);
    }

    [Fact]
    public async Task AddNote_appends_note_and_clears_input()
    {
        var note = new NoteDto(3, "saved", TestClock.UtcNow, TestClock.UtcNow);
        _noteService.AddNoteAsync(Hash, "saved").Returns(note);
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
        _noteService.GetNotesAsync(Hash).Returns([note]);
        await _sut.SelectHashAsync(Hash);

        await _sut.DeleteNote(note);

        Assert.Empty(_sut.Notes);
        await _noteService.Received(1).DeleteNoteAsync(note.Id);
    }

    [Fact]
    public async Task Mutations_raise_state_changed()
    {
        var note = new NoteDto(3, "saved", TestClock.UtcNow, TestClock.UtcNow);
        _noteService.AddNoteAsync(Hash, "saved").Returns(note);
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
}
