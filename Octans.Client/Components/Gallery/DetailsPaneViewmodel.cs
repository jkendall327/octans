using System.Threading.Channels;
using MudBlazor;
using Octans.Core.Notes;
using Octans.Core.Repositories;
using Octans.Core.Tags;

namespace Octans.Client.Components.Gallery;

public sealed class DetailsPaneViewmodel(
    INoteService noteService,
    ITagService tagService,
    ISnackbar snackbar,
    ChannelWriter<RepositoryChangeRequest> repositoryChannel) : ViewmodelBase
{
    public string? SelectedHash { get; private set; }
    public List<NoteDto> Notes { get; private set; } = [];
    public List<TagViewer.Tag> Tags { get; private set; } = [];
    public string NewNoteContent { get; set; } = string.Empty;
    public bool HasSelection => SelectedHash is not null;
    public bool CanAddNote => !string.IsNullOrWhiteSpace(NewNoteContent);

    public async Task SelectHashAsync(string? selectedHash)
    {
        SelectedHash = selectedHash;
        await LoadData();
        await NotifyStateChanged();
    }

    public async Task ArchiveImage()
    {
        if (SelectedHash is null)
        {
            return;
        }

        await repositoryChannel.WriteAsync(new(SelectedHash, RepositoryDestination.Archive));
        snackbar.Add("Archived", Severity.Success);
    }

    public async Task InboxImage()
    {
        if (SelectedHash is null)
        {
            return;
        }

        await repositoryChannel.WriteAsync(new(SelectedHash, RepositoryDestination.Inbox));
        snackbar.Add("Moved to Inbox", Severity.Success);
    }

    public async Task AddNote()
    {
        if (SelectedHash is null || string.IsNullOrWhiteSpace(NewNoteContent))
        {
            return;
        }

        try
        {
            var note = await noteService.AddNoteAsync(SelectedHash, NewNoteContent);
            Notes.Add(note);
            NewNoteContent = string.Empty;
            snackbar.Add("Note added", Severity.Success);
            await NotifyStateChanged();
        }
        catch (Exception ex)
        {
            snackbar.Add($"Failed to add note: {ex.Message}", Severity.Error);
        }
    }

    public async Task DeleteNote(NoteDto note)
    {
        try
        {
            await noteService.DeleteNoteAsync(note.Id);
            Notes.Remove(note);
            snackbar.Add("Note deleted", Severity.Success);
            await NotifyStateChanged();
        }
        catch (Exception ex)
        {
            snackbar.Add($"Failed to delete note: {ex.Message}", Severity.Error);
        }
    }

    private async Task LoadData()
    {
        if (SelectedHash is null)
        {
            Notes = [];
            Tags = [];
            NewNoteContent = string.Empty;
            return;
        }

        Notes = await noteService.GetNotesAsync(SelectedHash);
        var tagModels = await tagService.GetTagsForHashAsync(SelectedHash);
        Tags = tagModels.Select(t => new TagViewer.Tag(t.Namespace, t.Subtag, 0)).ToList();
    }
}
