using NativeHub.Models;
using NativeHub.Services;

namespace NativeHub.Tests;

[TestClass]
public sealed class PersistenceTests
{
    private string _folder = null!;
    private NoteService _notes = null!;

    [TestInitialize]
    public void Initialize()
    {
        _folder = Path.Combine(Path.GetTempPath(), "NativeHub.Tests", Guid.NewGuid().ToString("N"));
        _notes = new NoteService(new JsonStore(_folder));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    [TestMethod]
    public async Task NoteService_CreatesUpdatesAndDeletesWithoutDuplicates()
    {
        var note = new Note { Title = "First", Body = "Body" };
        await _notes.SaveAsync(note);
        note.Title = "Updated";
        await _notes.SaveAsync(note);

        var loaded = await _notes.GetAsync();
        Assert.HasCount(1, loaded);
        Assert.AreEqual("Updated", loaded[0].Title);

        await _notes.DeleteAsync(note.Id);
        Assert.IsEmpty(await _notes.GetAsync());
    }

    [TestMethod]
    public async Task JsonStore_RecoversDamagedJson()
    {
        Directory.CreateDirectory(_folder);
        await File.WriteAllTextAsync(Path.Combine(_folder, "notes.json"), "{broken");
        Assert.IsEmpty(await _notes.GetAsync());
        Assert.HasCount(1, Directory.GetFiles(_folder, "notes.json.damaged-*"));
    }
}
