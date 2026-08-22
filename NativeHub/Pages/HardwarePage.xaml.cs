using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NativeHub.Models;
using NativeHub.Services;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace NativeHub.Pages;

public sealed record HardwareGroupView(string Title, IReadOnlyList<HardwareItem> Items);

public sealed partial class HardwarePage : Page
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private IReadOnlyList<HardwareItem> _items = [];
    private bool _loading;
    private bool _active;

    public HardwarePage() => InitializeComponent();

    private async void Page_Loaded(object sender, RoutedEventArgs e) { _active = true; await LoadAsync(true); }
    private void Page_Unloaded(object sender, RoutedEventArgs e) => _active = false;
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshNowAsync();
    public Task RefreshNowAsync() => LoadAsync(true);

    private async Task LoadAsync(bool refreshInventory)
    {
        if (_loading || !_active) return;
        _loading = true;
        RefreshButton.IsEnabled = false;
        RefreshButton.Content = "Refreshing…";
        try
        {
            _items = await AppServices.Hardware.GetAsync(refreshInventory);
            HardwareGroups.ItemsSource = BuildGroups(_items);
            var summary = _items.Where(item => item.Group is "System" or "Processor" or "Graphics" or "Memory")
                .GroupBy(item => item.Name).Select(group => group.First()).Take(8);
            FastfetchText.Text = string.Join(Environment.NewLine, summary.Select(item => $"{item.Name,-20} {item.Value}"));
            UpdatedText.Text = $"Updated {DateTimeOffset.Now:T} · {_items.Count(item => item.Group == "Sensors"):N0} sensor readings · manual refresh";
        }
        catch (Exception ex) { (App.MainWindow as MainWindow)?.ShowMessage("Hardware refresh failed", ex.Message, InfoBarSeverity.Warning); }
        finally
        {
            _loading = false;
            if (_active)
            {
                RefreshButton.IsEnabled = true;
                RefreshButton.Content = "Refresh";
            }
        }
    }

    private static List<HardwareGroupView> BuildGroups(IEnumerable<HardwareItem> items)
    {
        var cards = new List<HardwareGroupView>();
        foreach (var group in items.GroupBy(item => item.Group))
        {
            var chunks = group.Chunk(7).ToList();
            for (var index = 0; index < chunks.Count; index++)
            {
                var title = chunks.Count == 1 ? group.Key : $"{group.Key} {index + 1}";
                cards.Add(new HardwareGroupView(title, chunks[index]));
            }
        }
        return cards;
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => ClipboardService.CopyText(
        string.Join(Environment.NewLine, _items.Select(item => $"{item.Group} | {item.Name}: {item.Value}")));

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker { SuggestedFileName = $"NativeHub-Hardware-{DateTime.Now:yyyyMMdd-HHmm}" };
            picker.FileTypeChoices.Add("JSON", [".json"]);
            var window = App.MainWindow;
            if (window is null) return;
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            await FileIO.WriteTextAsync(file, JsonSerializer.Serialize(_items, JsonOptions));
            (App.MainWindow as MainWindow)?.ShowMessage("Hardware report exported", file.Path, InfoBarSeverity.Success);
        }
        catch (Exception ex) { (App.MainWindow as MainWindow)?.ShowMessage("Export failed", ex.Message, InfoBarSeverity.Error); }
    }
}
