using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace NativeHub.Controls;

public sealed partial class NativOsWindow : UserControl
{
    private Point _pointerOrigin;
    private double _originLeft;
    private double _originTop;
    private double _originWidth;
    private double _originHeight;
    private bool _dragging;
    private bool _resizing;
    private bool _maximized;
    private (double Left, double Top, double Width, double Height) _restoreBounds;

    public NativOsWindow()
    {
        InitializeComponent();
        PointerPressed += (_, _) => Activated?.Invoke(this, EventArgs.Empty);
    }

    public string WindowId { get; private set; } = string.Empty;
    public bool IsMinimized { get; private set; }
    public UIElement? AppContent => AppContentPresenter.Content as UIElement;

    public event EventHandler? Activated;
    public event EventHandler? CloseRequested;
    public event EventHandler? Minimized;

    public void Configure(string id, string title, string glyph, UIElement content)
    {
        WindowId = id;
        TitleText.Text = title;
        TitleIcon.Glyph = glyph;
        AppContentPresenter.Content = content;
        AutomationProperties.SetName(this, $"{title} NativOS window");
    }

    public void RestoreFromTaskbar()
    {
        IsMinimized = false;
        Visibility = Visibility.Visible;
        Activated?.Invoke(this, EventArgs.Empty);
    }

    public void MinimizeToTaskbar()
    {
        IsMinimized = true;
        Visibility = Visibility.Collapsed;
        Minimized?.Invoke(this, EventArgs.Empty);
    }

    public void SetActive(bool active)
    {
        WindowBorder.BorderBrush = new SolidColorBrush(active
            ? Windows.UI.Color.FromArgb(255, 138, 190, 255)
            : Windows.UI.Color.FromArgb(255, 80, 101, 133));
    }

    private void TitleDragArea_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_maximized || IsButtonSource(e.OriginalSource as DependencyObject)) return;
        var canvas = Parent as Canvas;
        if (canvas is null) return;
        var point = e.GetCurrentPoint(canvas);
        if (!point.Properties.IsLeftButtonPressed) return;
        _dragging = true;
        _pointerOrigin = point.Position;
        _originLeft = SafeCoordinate(Canvas.GetLeft(this));
        _originTop = SafeCoordinate(Canvas.GetTop(this));
        TitleDragArea.CapturePointer(e.Pointer);
        Activated?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void TitleDragArea_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || Parent is not Canvas canvas) return;
        var position = e.GetCurrentPoint(canvas).Position;
        var maxLeft = Math.Max(0, canvas.ActualWidth - ActualWidth);
        var maxTop = Math.Max(0, canvas.ActualHeight - 40);
        Canvas.SetLeft(this, Math.Clamp(_originLeft + position.X - _pointerOrigin.X, 0, maxLeft));
        Canvas.SetTop(this, Math.Clamp(_originTop + position.Y - _pointerOrigin.Y, 0, maxTop));
    }

    private void TitleDragArea_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _dragging = false;
        TitleDragArea.ReleasePointerCapture(e.Pointer);
    }

    private void ResizeGrip_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_maximized || Parent is not Canvas canvas) return;
        var point = e.GetCurrentPoint(canvas);
        if (!point.Properties.IsLeftButtonPressed) return;
        _resizing = true;
        _pointerOrigin = point.Position;
        _originWidth = ActualWidth;
        _originHeight = ActualHeight;
        ResizeGrip.CapturePointer(e.Pointer);
        Activated?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void ResizeGrip_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizing || Parent is not Canvas canvas) return;
        var position = e.GetCurrentPoint(canvas).Position;
        Width = Math.Clamp(_originWidth + position.X - _pointerOrigin.X, MinWidth, Math.Max(MinWidth, canvas.ActualWidth - SafeCoordinate(Canvas.GetLeft(this))));
        Height = Math.Clamp(_originHeight + position.Y - _pointerOrigin.Y, MinHeight, Math.Max(MinHeight, canvas.ActualHeight - SafeCoordinate(Canvas.GetTop(this))));
    }

    private void ResizeGrip_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _resizing = false;
        ResizeGrip.ReleasePointerCapture(e.Pointer);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
        => MinimizeToTaskbar();

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (Parent is not Canvas canvas) return;
        if (!_maximized)
        {
            _restoreBounds = (SafeCoordinate(Canvas.GetLeft(this)), SafeCoordinate(Canvas.GetTop(this)), ActualWidth, ActualHeight);
            Canvas.SetLeft(this, 0);
            Canvas.SetTop(this, 0);
            Width = canvas.ActualWidth;
            Height = canvas.ActualHeight;
            _maximized = true;
        }
        else
        {
            Canvas.SetLeft(this, _restoreBounds.Left);
            Canvas.SetTop(this, _restoreBounds.Top);
            Width = _restoreBounds.Width;
            Height = _restoreBounds.Height;
            _maximized = false;
        }
        Activated?.Invoke(this, EventArgs.Empty);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private static bool IsButtonSource(DependencyObject? source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is Button) return true;
        return false;
    }

    private static double SafeCoordinate(double value) => double.IsNaN(value) ? 0 : value;
}
