using System.Globalization;
using System.Text.Json.Serialization;
using NativeHub.Services;

namespace NativeHub.Models;

public sealed record FileSearchResult(string Name, string FullPath, long Size, DateTimeOffset Modified, bool IsFolder)
{
    public string TypeLabel => IsFolder ? "Folder" : (Path.GetExtension(Name).TrimStart('.').ToUpperInvariant() is { Length: > 0 } extension ? extension : "File");
    public string SizeLabel => IsFolder ? "—" : UtilityFormatting.FormatBytes(Size);
    public string ModifiedLabel => Modified == DateTimeOffset.MinValue ? "Unknown" : Modified.ToString("g", CultureInfo.CurrentCulture);

}

public sealed record ClipboardEntry(string Id, string Kind, string Preview, DateTimeOffset Timestamp)
{
    public string TimestampLabel => Timestamp.ToString("g", CultureInfo.CurrentCulture);
}

public sealed class Note
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Untitled note";
    public string Body { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public bool IsPinned { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? ReminderAt { get; set; }
    public bool ReminderDelivered { get; set; }
}

public sealed record HardwareItem(string Group, string Name, string Value, string Detail = "")
{
    [JsonIgnore]
    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
}
public sealed record WeatherSnapshot(string City, double Temperature, double ApparentTemperature, int Humidity,
    double WindSpeed, double Precipitation, int WeatherCode, DateTimeOffset ObservedAt,
    IReadOnlyList<WeatherHour> Hourly, IReadOnlyList<WeatherDay> Forecast, string Sunrise, string Sunset, bool IsStale = false);
public sealed record WeatherHour(string Time, double Temperature, int WeatherCode, double PrecipitationProbability);
public sealed record WeatherDay(DateOnly Date, double Minimum, double Maximum, int WeatherCode);
public sealed record WeatherPlace(string Name, string Admin1, string Country, double Latitude, double Longitude, string Timezone)
{
    public string DisplayName => string.Join(", ", new[] { Name, Admin1, Country }
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.CurrentCultureIgnoreCase));

    public string RegionLabel => string.Join(" · ", new[] { Admin1, Country }
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.CurrentCultureIgnoreCase));
}
public sealed record WorldClock(string City, string TimeZoneId, string Glyph);
