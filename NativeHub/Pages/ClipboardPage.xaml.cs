using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NativeHub.Models;
using NativeHub.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace NativeHub.Pages;

public sealed partial class ClipboardPage : Page
{
    private IReadOnlyList<ClipboardEntry> _entries = [];

    public ClipboardPage() => InitializeComponent();
    private async void Page_Loaded(object sender, RoutedEventArgs e) => await RefreshNowAsync();
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshNowAsync();

    public async Task RefreshNowAsync()
    {
        try
        {
            var result = await AppServices.Clipboard.GetHistoryAsync();
            HistoryInfo.IsOpen = result.Status != ClipboardHistoryItemsResultStatus.Success;
            _entries = result.Entries;
            ApplyFilter();
        }
        catch (Exception ex)
        {
            HistoryInfo.Message = ex.Message;
            HistoryInfo.IsOpen = true;
        }
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void ApplyFilter()
    {
        var visible = _entries.Where(item => item.Preview.Contains(FilterBox.Text, StringComparison.OrdinalIgnoreCase)).ToList();
        HistoryList.ItemsSource = visible;
        HistoryStatus.Text = $"{visible.Count:N0} items";
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        if (!AppServices.Clipboard.Restore(id)) (App.MainWindow as MainWindow)?.ShowMessage("Clipboard item unavailable", "Windows may have removed this history item.", InfoBarSeverity.Warning);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id } && AppServices.Clipboard.Delete(id)) await RefreshNowAsync();
    }

    private async void Clear_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Clear clipboard history?",
            Content = "This removes unpinned Windows clipboard history and cannot be undone.",
            PrimaryButtonText = "Clear",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (!AppServices.Clipboard.Clear()) (App.MainWindow as MainWindow)?.ShowMessage("Clipboard history was not cleared", "Windows rejected the request.", InfoBarSeverity.Warning);
        await RefreshNowAsync();
    }

    private async void OpenSettings_Click(object sender, RoutedEventArgs e) => await Launcher.LaunchUriAsync(new Uri("ms-settings:clipboard"));
}
