using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Octans.Core.Models;
using Octans.Core.Notes;
using Xunit;

namespace Octans.Tests.Notes;

public class NoteServiceTests
{
    private readonly ServerDbContext _context;
    private readonly NoteService _service;

    public NoteServiceTests()
    {
        var options = new DbContextOptionsBuilder<ServerDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _context = new ServerDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();
        _service = new NoteService(_context);
    }

    [Fact]
    public async Task AddNote_ShouldAddNoteToHashItem()
    {
        // Arrange
        var hash = new byte[] { 0x01, 0x02, 0x03 };
        var hex = Convert.ToHexString(hash);
        var hashItem = new HashItem { Hash = hash };
        _context.Hashes.Add(hashItem);
        await _context.SaveChangesAsync();

        // Act
        var note = await _service.AddNoteAsync(hex, "Test Note");

        // Assert
        note.Should().NotBeNull();
        note.Content.Should().Be("Test Note");
        note.HashItemId.Should().Be(hashItem.Id);

        var savedNote = await _context.Notes.FirstOrDefaultAsync();
        savedNote.Should().NotBeNull();
        savedNote!.Content.Should().Be("Test Note");
    }

    [Fact]
    public async Task GetNotes_ShouldReturnNotesForHash()
    {
        // Arrange
        var hash = new byte[] { 0xAA, 0xBB };
        var hex = Convert.ToHexString(hash);
        var hashItem = new HashItem { Hash = hash };
        _context.Hashes.Add(hashItem);
        await _context.SaveChangesAsync();

        await _service.AddNoteAsync(hex, "Note 1");
        await _service.AddNoteAsync(hex, "Note 2");

        // Act
        var notes = await _service.GetNotesAsync(hex);

        // Assert
        notes.Should().HaveCount(2);
        string[] expected = ["Note 1", "Note 2"];
        notes.Select(n => n.Content).Should().Contain(expected);
    }

    [Fact]
    public async Task UpdateNote_ShouldUpdateContent()
    {
         // Arrange
        var hash = new byte[] { 0xCC };
        var hex = Convert.ToHexString(hash);
        var hashItem = new HashItem { Hash = hash };
        _context.Hashes.Add(hashItem);
        await _context.SaveChangesAsync();

        var note = await _service.AddNoteAsync(hex, "Original");

        // Act
        await _service.UpdateNoteAsync(note.Id, "Updated");

        // Assert
        var updatedNote = await _context.Notes.FindAsync(note.Id);
        updatedNote!.Content.Should().Be("Updated");
        updatedNote.LastModifiedAt.Should().BeAfter(note.CreatedAt);
    }

    [Fact]
    public async Task DeleteNote_ShouldRemoveNote()
    {
        // Arrange
        var hash = new byte[] { 0xDD };
        var hex = Convert.ToHexString(hash);
        var hashItem = new HashItem { Hash = hash };
        _context.Hashes.Add(hashItem);
        await _context.SaveChangesAsync();

        var note = await _service.AddNoteAsync(hex, "To Delete");

        // Act
        await _service.DeleteNoteAsync(note.Id);

        // Assert
        var deletedNote = await _context.Notes.FindAsync(note.Id);
        deletedNote.Should().BeNull();
    }
}
