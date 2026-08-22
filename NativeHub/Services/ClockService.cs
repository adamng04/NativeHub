using System.Globalization;
using System.Text;
using NativeHub.Models;

namespace NativeHub.Services;

public static class ClockService
{
    public static IReadOnlyList<WorldClock> Clocks { get; } =
    [
        new("Ho Chi Minh", "SE Asia Standard Time", "\uE909"),
        new("New York", "Eastern Standard Time", "\uE774"),
        new("Tokyo", "Tokyo Standard Time", "\uE909"),
        new("Amsterdam", "W. Europe Standard Time", "\uE774"),
        new("Madrid, Spain", "Romance Standard Time", "\uE774"),
        new("London", "GMT Standard Time", "\uE774"),
        new("Los Angeles", "Pacific Standard Time", "\uE774"),
        new("Sydney", "AUS Eastern Standard Time", "\uE909"),
        new("Dubai", "Arabian Standard Time", "\uE909"),
        new("Mumbai", "India Standard Time", "\uE909"),
        new("Singapore", "Singapore Standard Time", "\uE909"),
        new("São Paulo", "E. South America Standard Time", "\uE774"),
    ];

    public static DateTimeOffset GetTime(string id, DateTimeOffset? instant = null) =>
        TimeZoneInfo.ConvertTime(instant ?? DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(id));

    public static string Format(DateTimeOffset value, bool braille)
    {
        var text = value.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        return braille ? ToBraille(text) : text;
    }

    public static string ToBraille(string value)
    {
        const string digits = "\u281A\u2801\u2803\u2809\u2819\u2811\u280B\u281B\u2813\u280A";
        var result = new StringBuilder();
        var numericMode = false;
        foreach (var c in value)
        {
            if (char.IsDigit(c))
            {
                if (!numericMode) result.Append('\u283C');
                result.Append(digits[c - '0']);
                numericMode = true;
            }
            else { result.Append(c); numericMode = false; }
        }
        return result.ToString();
    }
}
