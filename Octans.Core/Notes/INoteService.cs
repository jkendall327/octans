using Octans.Core.Models;

namespace Octans.Core.Notes;

public interface INoteService
{
    Task<List<Note>> GetNotesAsync(string hash);
    Task<Note> AddNoteAsync(string hash, string content);
    Task UpdateNoteAsync(int noteId, string content);
    Task DeleteNoteAsync(int noteId);
}
