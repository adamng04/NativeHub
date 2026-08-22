using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NativeHub.Models;
using NativeHub.Services;
using System.Globalization;

namespace NativeHub.Pages;

public sealed record WeatherHourView(string Time, string Temperature, string Condition, string Rain);
public sealed record WeatherDayView(string Date, string Condition, string Range);

public sealed partial class WeatherPage : Page
{
    private CancellationTokenSource? _cancellation;
    private CancellationTokenSource? _suggestionCancellation;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _suggestionTimer;
    private WeatherPlace? _selectedPlace;
    private bool _active;

    public WeatherPage()
    {
        InitializeComponent();
        _suggestionTimer = DispatcherQueue.CreateTimer();
        _suggestionTimer.Interval = TimeSpan.FromMilliseconds(350);
        _suggestionTimer.IsRepeating = false;
        _suggestionTimer.Tick += async (_, _) => await LoadSuggestionsAsync();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e) { _active = true; CityBox.Text = AppServices.Settings.Current.WeatherCity; await LoadAsync(false); }
    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _active = false;
        _suggestionTimer.Stop();
        _suggestionCancellation?.Cancel();
        _cancellation?.Cancel();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshNowAsync();
    public Task RefreshNowAsync() => LoadAsync(true);

    private void CityBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        _selectedPlace = null;
        _suggestionTimer.Stop();
        _suggestionCancellation?.Cancel();
        if (sender.Text.Trim().Length < 2)
        {
            sender.ItemsSource = null;
            return;
        }
        _suggestionTimer.Start();
    }

    private void CityBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is not WeatherPlace place) return;
        _selectedPlace = place;
        sender.Text = place.DisplayName;
    }

    private async void CityBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var query = args.QueryText.Trim();
        if (query.Length < 2) return;
        try
        {
            var place = args.ChosenSuggestion as WeatherPlace ?? _selectedPlace ?? await AppServices.Weather.GeocodeAsync(query);
            if (place is null) { ShowError("City not found."); return; }
            _selectedPlace = place;
            sender.ItemsSource = null;
            sender.IsSuggestionListOpen = false;
            AppServices.Settings.Current.WeatherCity = place.DisplayName;
            AppServices.Settings.Current.WeatherLatitude = place.Latitude;
            AppServices.Settings.Current.WeatherLongitude = place.Longitude;
            CityBox.Text = place.DisplayName;
            await AppServices.Settings.SaveAsync();
            await LoadAsync(true);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async Task LoadSuggestionsAsync()
    {
        _suggestionTimer.Stop();
        _suggestionCancellation?.Cancel();
        _suggestionCancellation?.Dispose();
        _suggestionCancellation = new CancellationTokenSource();
        var query = CityBox.Text.Trim();
        if (query.Length < 2) return;
        try
        {
            var places = await AppServices.Weather.SuggestCitiesAsync(query, 7, _suggestionCancellation.Token);
            if (_active && string.Equals(query, CityBox.Text.Trim(), StringComparison.OrdinalIgnoreCase))
                CityBox.ItemsSource = places;
        }
        catch (OperationCanceledException) { }
        catch (HttpRequestException) { if (_active) CityBox.ItemsSource = null; }
    }

    private async Task LoadAsync(bool force)
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        try
        {
            var settings = AppServices.Settings.Current;
            var weather = await AppServices.Weather.GetAsync(settings.WeatherCity, settings.WeatherLatitude, settings.WeatherLongitude, force, _cancellation.Token);
            if (!_active) return;
            var fahrenheit = settings.TemperatureUnit == "F";
            var unit = fahrenheit ? "°F" : "°C";
            double Convert(double value) => UtilityFormatting.ConvertTemperature(value, fahrenheit);
            CityText.Text = weather.City;
            TemperatureText.Text = $"{Convert(weather.Temperature):0}{unit}";
            ConditionText.Text = UtilityFormatting.DescribeWeather(weather.WeatherCode);
            DetailsText.Text = $"Feels like {Convert(weather.ApparentTemperature):0}{unit}  ·  Humidity {weather.Humidity}%  ·  Wind {weather.WindSpeed:0.#} km/h  ·  Precipitation {weather.Precipitation:0.#} mm";
            SunText.Text = $"Sunrise {TimePart(weather.Sunrise)}  ·  Sunset {TimePart(weather.Sunset)}  ·  Updated {weather.ObservedAt:t}";
            HourlyItems.ItemsSource = (weather.Hourly ?? []).Select(hour => new WeatherHourView(hour.Time, $"{Convert(hour.Temperature):0}{unit}", UtilityFormatting.DescribeWeather(hour.WeatherCode), $"Rain {hour.PrecipitationProbability:0}%")).ToList();
            ForecastItems.ItemsSource = (weather.Forecast ?? []).Select(day => new WeatherDayView(day.Date.ToString("ddd, MMM d", CultureInfo.CurrentCulture), UtilityFormatting.DescribeWeather(day.WeatherCode), $"{Convert(day.Minimum):0}° / {Convert(day.Maximum):0}°")).ToList();
            WeatherInfo.IsOpen = weather.IsStale;
            WeatherInfo.Title = "Offline weather";
            WeatherInfo.Message = $"Showing cached data from {weather.ObservedAt:g}.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void ShowError(string text) { WeatherInfo.IsOpen = true; WeatherInfo.Title = "Weather unavailable"; WeatherInfo.Message = text; }
    private static string TimePart(string value) => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time.ToString("t", CultureInfo.CurrentCulture) : "Unknown";
}
