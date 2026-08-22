namespace NativeHub.Services;

public static class UtilityFormatting
{
    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }

    public static double ConvertTemperature(double celsius, bool fahrenheit) => fahrenheit ? celsius * 9 / 5 + 32 : celsius;

    public static string DescribeWeather(int code) => code switch
    {
        0 => "Clear sky", 1 or 2 => "Partly cloudy", 3 => "Overcast", 45 or 48 => "Fog",
        >= 51 and <= 67 => "Rain", >= 71 and <= 77 => "Snow", >= 80 and <= 82 => "Showers",
        >= 95 => "Thunderstorm", _ => "Mixed conditions",
    };
}
