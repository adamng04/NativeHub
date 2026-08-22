using System.Text.Json;
using Windows.Storage;

namespace NativeHub.Services;

public sealed class JsonStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _root;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonStore(string? root = null)
    {
        _root = root ?? GetDefaultRoot();
        Directory.CreateDirectory(_root);
    }

    private static string GetDefaultRoot()
    {
        try { return ApplicationData.Current.LocalFolder.Path; }
        catch (InvalidOperationException)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NativeHub");
        }
    }

    public async Task<T> LoadAsync<T>(string fileName, T fallback, CancellationToken token = default)
    {
        var path = Path.Combine(_root, fileName);
        if (!File.Exists(path)) return fallback;
        await _gate.WaitAsync(token);
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, Options, token) ?? fallback;
        }
        catch (JsonException)
        {
            var damaged = path + $".damaged-{DateTimeOffset.Now:yyyyMMddHHmmss}";
            File.Move(path, damaged, true);
            return fallback;
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync<T>(string fileName, T value, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            var path = Path.Combine(_root, fileName);
            var temp = path + ".tmp";
            await using (var stream = File.Create(temp)) await JsonSerializer.SerializeAsync(stream, value, Options, token);
            File.Move(temp, path, true);
        }
        finally { _gate.Release(); }
    }
}
