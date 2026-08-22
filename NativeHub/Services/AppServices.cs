using Microsoft.UI.Xaml;
using NativeHub.Services.NativOs;

namespace NativeHub.Services;

public sealed class AppSettings
{
    public string Theme { get; set; } = "System";
    public string TemperatureUnit { get; set; } = "C";
    public string ClockDigits { get; set; } = "Arabic";
    public string WeatherCity { get; set; } = "Ho Chi Minh City";
    public double WeatherLatitude { get; set; } = 10.8231;
    public double WeatherLongitude { get; set; } = 106.6297;
    public bool HideToTray { get; set; } = true;
    public bool HasSeenTrayTip { get; set; }
    public bool ShowBrailleNotice { get; set; } = true;
    public bool StartWithWindows { get; set; }
}

public sealed class SettingsService(JsonStore store)
{
    public AppSettings Current { get; private set; } = new();
    public async Task InitializeAsync() => Current = await store.LoadAsync("settings.json", new AppSettings());
    public Task SaveAsync() => store.SaveAsync("settings.json", Current);
    public static ElementTheme ToElementTheme(string theme) => theme switch
    {
        "Light" => ElementTheme.Light, "Dark" => ElementTheme.Dark, _ => ElementTheme.Default,
    };
}

public static class AppServices
{
    public static JsonStore Store { get; } = new();
    public static SettingsService Settings { get; } = new(Store);
    public static NoteService Notes { get; } = new(Store);
    public static FileSearchService Search { get; } = new();
    public static WeatherService Weather { get; } = new(Store);
    public static HardwareService Hardware { get; } = new();
    public static ClipboardService Clipboard { get; } = new();
    public static NativOsSessionService NativOs { get; } = new(Store);
}
