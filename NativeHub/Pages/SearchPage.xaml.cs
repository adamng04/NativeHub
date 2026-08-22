using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NativeHub.Models;
using NativeHub.Services;
using Windows.System;

namespace NativeHub.Pages;

public sealed partial class SearchPage : Page
{
    public static string? PendingScope { get; set; }
    private readonly ObservableCollection<FileSearchResult> _results = [];
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _debounce;
    private CancellationTokenSource? _searchCancellation;
    private bool _loaded;

    public SearchPage()
    {
        InitializeComponent();
        ResultsList.ItemsSource = _results;
        SortBox.SelectedIndex = 0;
        _debounce = DispatcherQueue.CreateTimer();
        _debounce.Interval = TimeSpan.FromMilliseconds(300);
        _debounce.IsRepeating = false;
        _debounce.Tick += async (_, _) => await SearchNowAsync();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _loaded = true;
        DependencyInfo.IsOpen = !AppServices.Search.IsEverythingAvailable;
        if (PendingScope is not null)
        {
            QueryBox.Text = $"path:\"{PendingScope}\" ";
            PathToggle.IsChecked = true;
            PendingScope = null;
        }
        FocusQuery();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _loaded = false;
        _debounce.Stop();
        _searchCancellation?.Cancel();
    }

    public void FocusQuery() => QueryBox.Focus(FocusState.Programmatic);
    private async void QueryBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) => await SearchNowAsync();
    private async void Search_Click(object sender, RoutedEventArgs e) => await SearchNowAsync();
    private void QueryBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) { if (_loaded && args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) { _debounce.Stop(); _debounce.Start(); } }
    private async void Option_Click(object sender, RoutedEventArgs e) => await SearchNowAsync();
    private async void SortBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_loaded) await SearchNowAsync(); }

    public async Task SearchNowAsync()
    {
        _debounce.Stop();
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        Busy.IsActive = true;
        ResultStatus.Text = "Searching…";
        try
        {
            var tag = (SortBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            var sort = Enum.TryParse<EverythingSort>(tag, out var parsed) ? parsed : EverythingSort.NameAscending;
            var values = await AppServices.Search.SearchAsync(QueryBox.Text, CaseToggle.IsChecked == true, RegexToggle.IsChecked == true,
                PathToggle.IsChecked == true, WordToggle.IsChecked == true, sort, _searchCancellation.Token);
            _results.Clear();
            foreach (var value in values) _results.Add(value);
            ResultStatus.Text = values.Count == 500 ? "Showing first 500 results" : $"{values.Count:N0} results";
            DependencyInfo.IsOpen = false;
        }
        catch (OperationCanceledException) { }
        catch (EverythingSearchException ex)
        {
            DependencyInfo.Message = ex.Message;
            DependencyInfo.IsOpen = true;
            ResultStatus.Text = "Search unavailable";
        }
        finally { Busy.IsActive = false; }
    }

    private void ResultsList_ItemClick(object sender, ItemClickEventArgs e) { if (e.ClickedItem is FileSearchResult result) Open(result); }
    private void ResultsList_KeyDown(object sender, KeyRoutedEventArgs e) { if (e.Key == VirtualKey.Enter && ResultsList.SelectedItem is FileSearchResult result) { Open(result); e.Handled = true; } }

    private static void Open(FileSearchResult result)
    {
        try { Process.Start(new ProcessStartInfo(result.FullPath) { UseShellExecute = true }); }
        catch (Exception ex) { (App.MainWindow as MainWindow)?.ShowMessage("Could not open item", ex.Message, InfoBarSeverity.Error); }
    }

    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path }) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path.Replace("\"", "", StringComparison.Ordinal)}\"") { UseShellExecute = true }); }
        catch (Exception ex) { (App.MainWindow as MainWindow)?.ShowMessage("Could not open File Explorer", ex.Message, InfoBarSeverity.Error); }
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e) { if (sender is Button { Tag: string path }) ClipboardService.CopyText(path); }
}
