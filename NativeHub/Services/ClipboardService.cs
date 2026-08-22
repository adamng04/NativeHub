using NativeHub.Models;
using Windows.ApplicationModel.DataTransfer;

namespace NativeHub.Services;

public sealed class ClipboardService
{
    private readonly Dictionary<string, ClipboardHistoryItem> _items = [];

    public async Task<(ClipboardHistoryItemsResultStatus Status, IReadOnlyList<ClipboardEntry> Entries)> GetHistoryAsync()
    {
        var result = await Clipboard.GetHistoryItemsAsync();
        if (result.Status != ClipboardHistoryItemsResultStatus.Success) return (result.Status, []);
        _items.Clear();
        var entries = new List<ClipboardEntry>();
        foreach (var item in result.Items)
        {
            _items[item.Id] = item;
            var content = item.Content;
            if (content.Contains(StandardDataFormats.Text))
            {
                var text = await content.GetTextAsync();
                entries.Add(new(item.Id, "Text", text.Length > 240 ? text[..240] + "…" : text, item.Timestamp));
            }
            else if (content.Contains(StandardDataFormats.Bitmap)) entries.Add(new(item.Id, "Image", "Image copied to clipboard", item.Timestamp));
            else if (content.Contains(StandardDataFormats.StorageItems)) entries.Add(new(item.Id, "Files", "Files copied to clipboard", item.Timestamp));
            else entries.Add(new(item.Id, "Other", "Unsupported clipboard format", item.Timestamp));
        }
        return (result.Status, entries);
    }

    public bool Restore(string id) => _items.TryGetValue(id, out var item) &&
        Clipboard.SetHistoryItemAsContent(item) == SetHistoryItemAsContentStatus.Success;

    public bool Delete(string id)
    {
        if (!_items.TryGetValue(id, out var item) || !Clipboard.DeleteItemFromHistory(item)) return false;
        _items.Remove(id);
        return true;
    }

    public static void CopyText(string text)
    {
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(text);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    public bool Clear()
    {
        var cleared = Clipboard.ClearHistory();
        if (cleared) _items.Clear();
        return cleared;
    }
}
