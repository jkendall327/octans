namespace Octans.Core.Notes;

public interface INoteService
{
    Task<List<NoteDto>> GetNotesAsync(string hash);
    Task<NoteDto> AddNoteAsync(string hash, string content);
    Task UpdateNoteAsync(int noteId, string content);
    Task DeleteNoteAsync(int noteId);
}

public sealed record NoteDto(
    int Id,
    string Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastModifiedAt);
