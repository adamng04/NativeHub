using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NativeHub.Models;
using NativeHub.Services;

namespace NativeHub.Pages;

public sealed partial class NotesPage : Page
{
    public static string? PendingText { get; set; }
    public static Guid? PendingNoteId { get; set; }
    public static bool CreateNewRequested { get; set; }
    public DateTimeOffset Today => DateTimeOffset.Now.Date;

    private List<Note> _notes = [];
    private Note? _selected;
    private bool _loading;
    private readonly DispatcherQueueTimer _timer;

    public NotesPage()
    {
        InitializeComponent();
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(600);
        _timer.IsRepeating = false;
        _timer.Tick += async (_, _) => await SaveNowAsync();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _notes = await AppServices.Notes.GetAsync();
        if (PendingText is not null)
        {
            var note = new Note { Body = PendingText, Title = "Dropped content" };
            PendingText = null;
            await AppServices.Notes.SaveAsync(note);
            _notes.Add(note);
            PendingNoteId = note.Id;
        }
        if (CreateNewRequested) { CreateNewRequested = false; await CreateNewAsync(); return; }
        ApplyFilter();
        var target = PendingNoteId is { } id ? _notes.FirstOrDefault(note => note.Id == id) : _notes.FirstOrDefault();
        PendingNoteId = null;
        if (target is not null) NotesList.SelectedItem = target;
    }

    private async void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        await SaveNowAsync();
    }

    private void ApplyFilter()
    {
        var visible = _notes.Where(note => (note.Title + note.Body + note.Tags).Contains(FilterBox.Text, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(note => note.IsPinned).ThenByDescending(note => note.UpdatedAt).ToList();
        NotesList.ItemsSource = visible;
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private async void New_Click(object sender, RoutedEventArgs e) => await CreateNewAsync();

    public async Task CreateNewAsync()
    {
        var note = new Note();
        _notes.Add(note);
        await AppServices.Notes.SaveAsync(note);
        ApplyFilter();
        NotesList.SelectedItem = note;
        TitleBox.Focus(FocusState.Programmatic);
        TitleBox.SelectAll();
    }

    private void NotesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = NotesList.SelectedItem as Note;
        _loading = true;
        TitleBox.Text = _selected?.Title ?? "";
        TagsBox.Text = _selected?.Tags ?? "";
        BodyBox.Text = _selected?.Body ?? "";
        PinToggle.IsChecked = _selected?.IsPinned;
        ReminderDate.Date = _selected?.ReminderAt?.Date;
        ReminderTime.Time = _selected?.ReminderAt?.TimeOfDay ?? new TimeSpan(DateTimeOffset.Now.Hour, DateTimeOffset.Now.Minute, 0);
        UpdateReminderStatus();
        _loading = false;
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _selected is null) return;
        _selected.Title = string.IsNullOrWhiteSpace(TitleBox.Text) ? "Untitled note" : TitleBox.Text;
        _selected.Tags = TagsBox.Text;
        _selected.Body = BodyBox.Text;
        QueueSave();
    }

    private void QueueSave()
    {
        SaveStatus.Text = "Saving…";
        _timer.Stop();
        _timer.Start();
    }

    public async Task SaveNowAsync()
    {
        _timer.Stop();
        if (_selected is null) return;
        await AppServices.Notes.SaveAsync(_selected);
        SaveStatus.Text = $"Saved {_selected.UpdatedAt:t}";
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        _selected.IsPinned = PinToggle.IsChecked == true;
        QueueSave();
        ApplyFilter();
        NotesList.SelectedItem = _selected;
    }

    private async void SetReminder_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || ReminderDate.Date is not { } date) return;
        var local = date.Date + ReminderTime.Time;
        _selected.ReminderAt = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        _selected.ReminderDelivered = false;
        await SaveNowAsync();
        UpdateReminderStatus();
    }

    private async void ClearReminder_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        _selected.ReminderAt = null;
        _selected.ReminderDelivered = false;
        ReminderDate.Date = null;
        await SaveNowAsync();
        UpdateReminderStatus();
    }

    private void UpdateReminderStatus() => ReminderStatus.Text = _selected?.ReminderAt is { } value ? $"Reminder {value:g}" : "No reminder";

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Delete this note?", Content = _selected.Title, PrimaryButtonText = "Delete", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await AppServices.Notes.DeleteAsync(_selected.Id);
        _notes.Remove(_selected);
        _selected = null;
        ApplyFilter();
        if (_notes.Count > 0) NotesList.SelectedIndex = 0;
    }
}
