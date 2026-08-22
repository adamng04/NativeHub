using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NativeHub.Controls;
using NativeHub.Services;
using NativeHub.Services.NativOs;
using System.Globalization;

namespace NativeHub.Pages;

public sealed partial class NativOsPage : Page
{
    private readonly Dictionary<string, NativOsWindow> _windows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Button> _taskButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherQueueTimer _clockTimer;
    private int _nextWindowOffset;
    private int _topZ;
    private bool _active;

    public NativOsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        AppServices.NativOs.StateChanged += NativOs_StateChanged;
        _clockTimer = DispatcherQueue.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.Tick += (_, _) => UpdateClock();
        RenderPowerState();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _active = true;
        RenderPowerState();
        UpdateClock();
        if (AppServices.NativOs.PowerState == NativOsPowerState.Running) _clockTimer.Start();
        SetGameActivity(true);
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _active = false;
        _clockTimer.Stop();
        SetGameActivity(false);
    }

    private async void PowerButton_Click(object sender, RoutedEventArgs e)
    {
        PowerButton.IsEnabled = false;
        try { await AppServices.NativOs.PowerOnAsync(); }
        finally { PowerButton.IsEnabled = true; }
    }

    private void NativOs_StateChanged(object? sender, EventArgs e)
    {
        if (DispatcherQueue.HasThreadAccess) RenderPowerState();
        else DispatcherQueue.TryEnqueue(RenderPowerState);
    }

    private void RenderPowerState()
    {
        var state = AppServices.NativOs.PowerState;
        PowerLayer.Visibility = state == NativOsPowerState.Off ? Visibility.Visible : Visibility.Collapsed;
        BootLayer.Visibility = state == NativOsPowerState.Booting ? Visibility.Visible : Visibility.Collapsed;
        DesktopLayer.Visibility = state == NativOsPowerState.Running ? Visibility.Visible : Visibility.Collapsed;

        if (state == NativOsPowerState.Booting)
        {
            BootText.Text = string.Join(Environment.NewLine, AppServices.NativOs.BootLines);
            BootProgressBar.Value = AppServices.NativOs.BootProgress;
            BootStatusText.Text = $"Power-on self test · {AppServices.NativOs.BootProgress}%";
            BootScroller.UpdateLayout();
            BootScroller.ChangeView(null, BootScroller.ScrollableHeight, null, true);
        }
        else if (state == NativOsPowerState.Running)
        {
            StartMenu.Visibility = Visibility.Collapsed;
            UpdateClock();
            if (_active) _clockTimer.Start();
        }
        else
        {
            _clockTimer.Stop();
        }
    }

    private void StartButton_Click(object sender, RoutedEventArgs e) =>
        StartMenu.Visibility = StartMenu.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    private void DesktopIcon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: { } tag }) return;
        StartMenu.Visibility = Visibility.Collapsed;
        OpenWindow(tag.ToString() ?? "computer");
    }

    private void OpenWindow(string id)
    {
        if (_windows.TryGetValue(id, out var existing))
        {
            existing.RestoreFromTaskbar();
            BringToFront(existing);
            UpdateTaskButton(existing);
            return;
        }

        var (title, glyph, width, height, content) = CreateWindowContent(id);
        var availableWidth = WindowCanvas.ActualWidth > 0 ? WindowCanvas.ActualWidth : width;
        var availableHeight = WindowCanvas.ActualHeight > 0 ? WindowCanvas.ActualHeight : height;
        var window = new NativOsWindow
        {
            Width = Math.Max(360, Math.Min(width, availableWidth)),
            Height = Math.Max(250, Math.Min(height, availableHeight)),
        };
        window.Configure(id, title, glyph, content);
        window.Loaded += (_, _) => FitWindowToCanvas(window);
        window.Activated += (_, _) =>
        {
            BringToFront(window);
            if (content is INativOsAppLifecycle activatedLifecycle) activatedLifecycle.SetActive(_active);
            UpdateTaskButton(window);
        };
        window.Minimized += (_, _) =>
        {
            if (content is INativOsAppLifecycle minimizedLifecycle) minimizedLifecycle.SetActive(false);
            UpdateTaskButton(window);
        };
        window.CloseRequested += (_, _) => CloseWindow(window);
        _windows[id] = window;
        WindowCanvas.Children.Add(window);
        if (content is INativOsAppLifecycle lifecycle) lifecycle.SetActive(_active);
        var offset = _nextWindowOffset++ % 7;
        Canvas.SetLeft(window, Math.Max(8, 34 + offset * 24));
        Canvas.SetTop(window, Math.Max(8, 24 + offset * 20));
        BringToFront(window);

        var taskButton = new Button
        {
            Content = title,
            Tag = id,
            MaxWidth = 180,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        ToolTipService.SetToolTip(taskButton, $"Show {title}");
        taskButton.Click += (_, _) =>
        {
            if (!_windows.TryGetValue(id, out var target)) return;
            if (!target.IsMinimized && Canvas.GetZIndex(target) == _topZ)
            {
                target.MinimizeToTaskbar();
                UpdateTaskButton(target);
                return;
            }
            target.RestoreFromTaskbar();
            BringToFront(target);
            UpdateTaskButton(target);
        };
        _taskButtons[id] = taskButton;
        TaskButtonsPanel.Children.Add(taskButton);
        UpdateTaskButton(window);
    }

    private static (string Title, string Glyph, double Width, double Height, UIElement Content) CreateWindowContent(string id) => id switch
    {
        "minefield" => ("Minefield", "\uE815", 700, 600, new MinefieldControl()),
        "falling" => ("Falling Blocks", "\uECA5", 700, 620, new FallingBlocksControl()),
        "blockworld" => ("BlockWorld", "\uE719", 880, 640, new VoxelSandboxControl()),
        _ => ("Computer", "\uE7F8", 560, 410, CreateComputerPanel()),
    };

    private static StackPanel CreateComputerPanel()
    {
        var panel = new StackPanel { Padding = new Thickness(22), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "NativOS", FontSize = 30, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Version 0.98 · Native Systems Desktop", Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 181, 201, 232)) });
        panel.Children.Add(new Border
        {
            Height = 1,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 89, 108, 137)),
        });
        panel.Children.Add(new TextBlock { Text = "This is an interactive fictional desktop environment inside NativeHub. It does not boot, install, emulate, or contain a real operating system.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Processor\tVirtual RISC 200\nMemory\t\t64 MB\nDisplay\t\tNativGA 32\nSystem disk\t512 MB", FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono"), IsTextSelectionEnabled = true });
        return panel;
    }

    private void BringToFront(NativOsWindow window)
    {
        if (window.Visibility != Visibility.Visible) window.Visibility = Visibility.Visible;
        Canvas.SetZIndex(window, ++_topZ);
        foreach (var value in _windows.Values) value.SetActive(ReferenceEquals(value, window));
    }

    private void CloseWindow(NativOsWindow window)
    {
        if (window.AppContent is INativOsAppLifecycle lifecycle) lifecycle.Close();
        _windows.Remove(window.WindowId);
        WindowCanvas.Children.Remove(window);
        if (_taskButtons.Remove(window.WindowId, out var taskButton)) TaskButtonsPanel.Children.Remove(taskButton);
    }

    private void UpdateTaskButton(NativOsWindow window)
    {
        if (_taskButtons.TryGetValue(window.WindowId, out var button)) button.Opacity = window.IsMinimized ? 0.62 : 1;
    }

    private async void ShutDown_Click(object sender, RoutedEventArgs e)
    {
        StartMenu.Visibility = Visibility.Collapsed;
        (App.MainWindow as MainWindow)?.SetNativOsFullScreen(false);
        SetGameActivity(false);
        foreach (var window in _windows.Values.ToList()) CloseWindow(window);
        await AppServices.NativOs.SaveAsync();
        AppServices.NativOs.ShutDown();
    }

    private void FullScreenButton_Click(object sender, RoutedEventArgs e) =>
        (App.MainWindow as MainWindow)?.ToggleNativOsFullScreen();

    public void SetFullScreenState(bool fullScreen)
    {
        FullScreenIcon.Glyph = fullScreen ? "\uE73F" : "\uE740";
        AutomationProperties.SetName(FullScreenButton, fullScreen ? "Exit borderless full screen" : "Enter borderless full screen");
        ToolTipService.SetToolTip(FullScreenButton, fullScreen ? "Exit full screen (F11)" : "Borderless full screen (F11)");
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        TaskbarClock.Text = now.ToString("h:mm tt", CultureInfo.CurrentCulture);
        TaskbarDate.Text = now.ToString("d", CultureInfo.CurrentCulture);
    }

    private void WindowCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        foreach (var window in _windows.Values) FitWindowToCanvas(window);
    }

    private void FitWindowToCanvas(NativOsWindow window)
    {
        var availableWidth = WindowCanvas.ActualWidth;
        var availableHeight = WindowCanvas.ActualHeight;
        if (availableWidth <= 0 || availableHeight <= 0) return;
        if (window.Width > availableWidth) window.Width = Math.Max(window.MinWidth, availableWidth);
        if (window.Height > availableHeight) window.Height = Math.Max(window.MinHeight, availableHeight);
        Canvas.SetLeft(window, Math.Clamp(double.IsNaN(Canvas.GetLeft(window)) ? 0 : Canvas.GetLeft(window), 0, Math.Max(0, availableWidth - window.Width)));
        Canvas.SetTop(window, Math.Clamp(double.IsNaN(Canvas.GetTop(window)) ? 0 : Canvas.GetTop(window), 0, Math.Max(0, availableHeight - window.Height)));
    }

    private void SetGameActivity(bool active)
    {
        foreach (var window in _windows.Values)
            if (window.AppContent is INativOsAppLifecycle lifecycle) lifecycle.SetActive(active);
    }

    public void ReleaseTransientInput() => SetGameActivity(false);
}

public interface INativOsAppLifecycle
{
    void SetActive(bool active);
    void Close();
}
