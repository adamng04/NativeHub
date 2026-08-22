using NativeHub.Models;

namespace NativeHub.Services;

public sealed class NoteService(JsonStore store)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task<List<Note>> GetAsync() => store.LoadAsync("notes.json", new List<Note>());

    public async Task SaveAsync(Note note)
    {
        await _gate.WaitAsync();
        try
        {
            var notes = await GetAsync();
            note.UpdatedAt = DateTimeOffset.Now;
            var index = notes.FindIndex(item => item.Id == note.Id);
            if (index >= 0) notes[index] = note; else notes.Add(note);
            await store.SaveAsync("notes.json", notes);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(Guid id)
    {
        await _gate.WaitAsync();
        try
        {
            var notes = await GetAsync();
            notes.RemoveAll(item => item.Id == id);
            await store.SaveAsync("notes.json", notes);
        }
        finally { _gate.Release(); }
    }
}
