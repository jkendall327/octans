using Microsoft.EntityFrameworkCore;
using Octans.Data.Models;

namespace Octans.Core.Notes;

public class NoteService(ServerDbContext context, TimeProvider timeProvider) : INoteService
{
    public async Task<List<Note>> GetNotesAsync(string hash)
    {
        var bytes = Convert.FromHexString(hash);
        var hashItem = await context.Hashes
            .Include(h => h.Notes)
            .FirstOrDefaultAsync(h => h.Hash == bytes);

        return hashItem?.Notes.ToList() ?? new List<Note>();
    }

    public async Task<Note> AddNoteAsync(string hash, string content)
    {
        var bytes = Convert.FromHexString(hash);
        var hashItem = await context.Hashes
            .FirstOrDefaultAsync(h => h.Hash == bytes) ?? throw new ArgumentException("Hash not found");

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var note = new Note
        {
            HashItemId = hashItem.Id,
            Content = content,
            CreatedAt = now,
            LastModifiedAt = now
        };

        context.Notes.Add(note);
        await context.SaveChangesAsync();
        return note;
    }

    public async Task UpdateNoteAsync(int noteId, string content)
    {
        var note = await context.Notes.FindAsync(noteId) ?? throw new ArgumentException("Note not found");
        note.Content = content;
        note.LastModifiedAt = timeProvider.GetUtcNow().UtcDateTime;
        await context.SaveChangesAsync();
    }

    public async Task DeleteNoteAsync(int noteId)
    {
        var note = await context.Notes.FindAsync(noteId);
        if (note != null)
        {
            context.Notes.Remove(note);
            await context.SaveChangesAsync();
        }
    }
}
