using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NativeHub.Models;
using NativeHub.Services;

namespace NativeHub.Pages;

public sealed class ClockCardView(WorldClock clock) : INotifyPropertyChanged
{
    private static readonly FontFamily ArabicFont = new("Segoe UI Variable Display");
    private static readonly FontFamily BrailleFont = new("Segoe UI Symbol");
    private string _timeText = string.Empty;
    private string _dateText = string.Empty;
    private string _accessibleName = string.Empty;
    private FontFamily _timeFontFamily = ArabicFont;
    private double _timeFontSize = 40;

    public string City => clock.City;
    public string TimeText { get => _timeText; private set => Set(ref _timeText, value); }
    public string DateText { get => _dateText; private set => Set(ref _dateText, value); }
    public string AccessibleName { get => _accessibleName; private set => Set(ref _accessibleName, value); }
    public FontFamily TimeFontFamily { get => _timeFontFamily; private set => Set(ref _timeFontFamily, value); }
    public double TimeFontSize { get => _timeFontSize; private set => Set(ref _timeFontSize, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Update(bool braille)
    {
        var time = ClockService.GetTime(clock.TimeZoneId);
        TimeText = ClockService.Format(time, braille);
        DateText = time.ToString("dddd, MMMM d · zzz", CultureInfo.CurrentCulture);
        AccessibleName = $"{clock.City}, {time.ToString("T", CultureInfo.CurrentCulture)}";
        TimeFontFamily = braille ? BrailleFont : ArabicFont;
        TimeFontSize = braille ? 33 : 40;
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed partial class ClockPage : Page
{
    private readonly DispatcherQueueTimer _timer;
    private readonly IReadOnlyList<ClockCardView> _cards;
    private bool _loading;

    public ClockPage()
    {
        InitializeComponent();
        _cards = ClockService.Clocks.Select(clock => new ClockCardView(clock)).ToList();
        ClockItems.ItemsSource = _cards;
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => Render();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _loading = true;
        DigitsBox.SelectedIndex = AppServices.Settings.Current.ClockDigits == "Braille" ? 1 : 0;
        _loading = false;
        UpdateBrailleNotice();
        Render();
        _timer.Start();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e) => _timer.Stop();

    private async void DigitsBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        AppServices.Settings.Current.ClockDigits = IsBrailleSelected ? "Braille" : "Arabic";
        await AppServices.Settings.SaveAsync();
        UpdateBrailleNotice();
        Render();
    }

    private async void BrailleInfoBar_DontShowAgain_Click(object sender, RoutedEventArgs e)
    {
        AppServices.Settings.Current.ShowBrailleNotice = false;
        await AppServices.Settings.SaveAsync();
        BrailleInfoBar.IsOpen = false;
    }

    public void RefreshNow() => Render();

    private bool IsBrailleSelected => (DigitsBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "Braille";

    private void UpdateBrailleNotice() =>
        BrailleInfoBar.IsOpen = IsBrailleSelected && AppServices.Settings.Current.ShowBrailleNotice;

    private void Render()
    {
        var braille = AppServices.Settings.Current.ClockDigits == "Braille";
        foreach (var card in _cards) card.Update(braille);
    }
}
