using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NativeHub.Pages;
using NativeHub.Services;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using WinRT.Interop;

namespace NativeHub;

public sealed partial class MainWindow : Window
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _reminderTimer;
    private NativeShellService? _nativeShell;
    private bool _allowClose;
    private bool _remindersChecking;
    private bool _cleanedUp;
    private bool _nativOsFullScreen;
    private AppWindowPresenterKind _presenterBeforeNativOsFullScreen = AppWindowPresenterKind.Overlapped;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        TrayTip.Target = AppTitleBar;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/AppIcon.ico");
        SizeAndCenterWindow();
        AppWindow.Closing += AppWindow_Closing;
        Activated += MainWindow_Activated;
        _reminderTimer = DispatcherQueue.CreateTimer();
        _reminderTimer.Interval = TimeSpan.FromSeconds(30);
        _reminderTimer.Tick += async (_, _) => await CheckRemindersAsync();
    }

    private void SizeAndCenterWindow()
    {
        const double preferredWidth = 1064;
        const double preferredHeight = 656;
        var scale = GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96d;
        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        var workArea = displayArea.WorkArea;
        var width = Math.Min((int)Math.Round(preferredWidth * scale), workArea.Width);
        var height = Math.Min((int)Math.Round(preferredHeight * scale), workArea.Height);
        var x = workArea.X + (workArea.Width - width) / 2;
        var y = workArea.Y + (workArea.Height - height) / 2;
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));
    }

    private async void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= MainWindow_Activated;
        await AppServices.Settings.InitializeAsync();
        RootGrid.RequestedTheme = SettingsService.ToElementTheme(AppServices.Settings.Current.Theme);
        _nativeShell = new NativeShellService(this);
        _nativeShell.HotkeyPressed += (_, _) => NavigateTo("search");
        _nativeShell.CommandInvoked += NativeShell_CommandInvoked;
        if (!_nativeShell.IsHotkeyRegistered)
            ShowMessage("Shortcut unavailable", "Ctrl+Alt+Space is already used by another app.", InfoBarSeverity.Warning);
        NavigateTo("search");
        _reminderTimer.Start();
        await CheckRemindersAsync();
    }

    private void NativeShell_CommandInvoked(object? sender, ShellCommand command) => DispatcherQueue.TryEnqueue(async () =>
    {
        switch (command)
        {
            case ShellCommand.Open: ShowAndActivate(); break;
            case ShellCommand.Search: ShowAndActivate(); NavigateTo("search"); break;
            case ShellCommand.Clipboard: ShowAndActivate(); NavigateTo("clipboard"); break;
            case ShellCommand.NewNote:
                ShowAndActivate();
                NavigateTo("notes");
                if (NavFrame.Content is NotesPage notes) await notes.CreateNewAsync(); else NotesPage.CreateNewRequested = true;
                break;
            case ShellCommand.ToggleTheme:
                AppServices.Settings.Current.Theme = AppServices.Settings.Current.Theme == "Dark" ? "Light" : "Dark";
                await AppServices.Settings.SaveAsync();
                ApplyTheme();
                break;
            case ShellCommand.Exit: Exit(); break;
        }
    });

    public void ShowMessage(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        ShellInfoBar.Title = title;
        ShellInfoBar.Message = message;
        ShellInfoBar.Severity = severity;
        ShellInfoBar.IsOpen = true;
    }

    public void ApplyTheme() => RootGrid.RequestedTheme = SettingsService.ToElementTheme(AppServices.Settings.Current.Theme);

    public void ShowAndActivate()
    {
        _nativeShell?.Show();
        AppWindow.Show();
        Activate();
    }

    public void NavigateTo(string tag)
    {
        if (_nativOsFullScreen && !string.Equals(tag, "nativos", StringComparison.OrdinalIgnoreCase))
            SetNativOsFullScreen(false);

        var type = tag switch
        {
            "search" => typeof(SearchPage), "clipboard" => typeof(ClipboardPage), "notes" => typeof(NotesPage),
            "hardware" => typeof(HardwarePage), "weather" => typeof(WeatherPage), "clock" => typeof(ClockPage),
            "nativos" => typeof(NativOsPage),
            "settings" => typeof(SettingsPage), _ => typeof(SearchPage),
        };
        if (NavFrame.CurrentSourcePageType != type) NavFrame.Navigate(type);

        if (tag == "settings") NavView.SelectedItem = NavView.SettingsItem;
        else
        {
            var item = NavView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(value => value.Tag?.ToString() == tag);
            if (item is not null && !ReferenceEquals(NavView.SelectedItem, item)) NavView.SelectedItem = item;
        }
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args) => NavView.IsPaneOpen = !NavView.IsPaneOpen;
    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args) =>
        NavigateTo(args.IsSettingsSelected ? "settings" : (args.SelectedItem as NavigationViewItem)?.Tag?.ToString() ?? "search");

    private void NavigationShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        var index = (int)sender.Key - (int)VirtualKey.Number1;
        if (index >= 0 && index < NavView.MenuItems.Count) NavView.SelectedItem = NavView.MenuItems[index];
        args.Handled = true;
    }

    private async void CommandShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        switch (sender.Key)
        {
            case VirtualKey.F:
                NavigateTo("search");
                if (NavFrame.Content is SearchPage search) search.FocusQuery();
                break;
            case VirtualKey.N:
                NavigateTo("notes");
                if (NavFrame.Content is NotesPage notes) await notes.CreateNewAsync(); else NotesPage.CreateNewRequested = true;
                break;
            case VirtualKey.V: NavigateTo("clipboard"); break;
            case VirtualKey.S:
                if (NavFrame.Content is NotesPage saveable) await saveable.SaveNowAsync();
                break;
            case VirtualKey.F5:
                await RefreshCurrentPageAsync();
                break;
            case VirtualKey.F11:
                if (NavFrame.Content is NativOsPage) ToggleNativOsFullScreen();
                break;
            case VirtualKey.Escape:
                if (_nativOsFullScreen) SetNativOsFullScreen(false);
                else if (ShellInfoBar.IsOpen) ShellInfoBar.IsOpen = false;
                else if (NavView.IsPaneOpen) NavView.IsPaneOpen = false;
                break;
        }
        args.Handled = true;
    }

    public bool IsNativOsFullScreen => _nativOsFullScreen;

    public void ToggleNativOsFullScreen() => SetNativOsFullScreen(!_nativOsFullScreen);

    public void SetNativOsFullScreen(bool fullScreen)
    {
        if (_nativOsFullScreen == fullScreen || (fullScreen && NavFrame.Content is not NativOsPage)) return;

        try
        {
            if (fullScreen)
            {
                _presenterBeforeNativOsFullScreen = AppWindow.Presenter.Kind;
                AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            }
            else
            {
                var presenter = _presenterBeforeNativOsFullScreen == AppWindowPresenterKind.FullScreen
                    ? AppWindowPresenterKind.Overlapped
                    : _presenterBeforeNativOsFullScreen;
                AppWindow.SetPresenter(presenter);
            }

            _nativOsFullScreen = fullScreen;
            RootGrid.RowDefinitions[0].Height = new GridLength(fullScreen ? 0 : 48);
            AppTitleBar.Visibility = fullScreen ? Visibility.Collapsed : Visibility.Visible;
            NavView.IsPaneVisible = !fullScreen;
            (NavFrame.Content as NativOsPage)?.SetFullScreenState(fullScreen);
        }
        catch (Exception ex)
        {
            _nativOsFullScreen = false;
            RootGrid.RowDefinitions[0].Height = new GridLength(48);
            AppTitleBar.Visibility = Visibility.Visible;
            NavView.IsPaneVisible = true;
            ShowMessage("Full screen unavailable", ex.Message, InfoBarSeverity.Warning);
        }
    }

    private async Task RefreshCurrentPageAsync()
    {
        switch (NavFrame.Content)
        {
            case SearchPage search: await search.SearchNowAsync(); break;
            case ClipboardPage clipboard: await clipboard.RefreshNowAsync(); break;
            case HardwarePage hardware: await hardware.RefreshNowAsync(); break;
            case WeatherPage weather: await weather.RefreshNowAsync(); break;
            case ClockPage clock: clock.RefreshNow(); break;
        }
    }

    private async Task CheckRemindersAsync()
    {
        if (_remindersChecking) return;
        _remindersChecking = true;
        try
        {
            var due = (await AppServices.Notes.GetAsync()).Where(note => note.ReminderAt <= DateTimeOffset.Now && !note.ReminderDelivered).ToList();
            foreach (var note in due)
            {
                if (!App.TryShowNotification(note.Title, string.IsNullOrWhiteSpace(note.Body) ? "Quick note reminder" : note.Body, note)) continue;
                note.ReminderDelivered = true;
                await AppServices.Notes.SaveAsync(note);
            }
        }
        catch (Exception ex) { ShowMessage("Reminder check failed", ex.Message, InfoBarSeverity.Warning); }
        finally { _remindersChecking = false; }
    }

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose || !AppServices.Settings.Current.HideToTray) { Cleanup(); return; }
        args.Cancel = true;
        if (_nativOsFullScreen) SetNativOsFullScreen(false);
        if (!AppServices.Settings.Current.HasSeenTrayTip)
        {
            AppServices.Settings.Current.HasSeenTrayTip = true;
            await AppServices.Settings.SaveAsync();
            TrayTip.IsOpen = true;
            return;
        }
        _nativeShell?.Hide();
    }

    public void Exit()
    {
        _allowClose = true;
        Cleanup();
        Close();
    }

    private void Cleanup()
    {
        if (_cleanedUp) return;
        _cleanedUp = true;
        if (_nativOsFullScreen) SetNativOsFullScreen(false);
        (NavFrame.Content as NativOsPage)?.ReleaseTransientInput();
        _reminderTimer.Stop();
        _nativeShell?.Dispose();
        AppServices.Hardware.Dispose();
        App.ShutdownIntegrations();
    }

    private void RootGrid_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems) || e.DataView.Contains(StandardDataFormats.Text))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Search a folder or add paths/text to a quick note";
            e.DragUIOverride.IsCaptionVisible = true;
        }
    }

    private async void RootGrid_Drop(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var folder = items.OfType<StorageFolder>().FirstOrDefault();
            if (folder is not null)
            {
                SearchPage.PendingScope = folder.Path;
                NavigateTo("search");
                ShowMessage("Search scope", folder.Path);
            }
            else
            {
                NotesPage.PendingText = string.Join(Environment.NewLine, items.Select(item => item.Path));
                NavigateTo("notes");
            }
        }
        else if (e.DataView.Contains(StandardDataFormats.Text))
        {
            NotesPage.PendingText = await e.DataView.GetTextAsync();
            NavigateTo("notes");
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);
}
