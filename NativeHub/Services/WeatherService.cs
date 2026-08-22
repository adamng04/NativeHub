using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using NativeHub.Models;

namespace NativeHub.Services;

public sealed class WeatherService(JsonStore store)
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(12) };

    public async Task<WeatherSnapshot> GetAsync(string city, double latitude, double longitude, bool force = false, CancellationToken token = default)
    {
        var cached = await store.LoadAsync<WeatherSnapshot?>("weather-cache.json", null, token);
        if (!force && cached is not null && cached.City == city && DateTimeOffset.Now - cached.ObservedAt < TimeSpan.FromMinutes(30)) return cached;
        try
        {
            var url = FormattableString.Invariant($"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&current=temperature_2m,relative_humidity_2m,apparent_temperature,precipitation,weather_code,wind_speed_10m&hourly=temperature_2m,precipitation_probability,weather_code&forecast_hours=24&daily=weather_code,temperature_2m_max,temperature_2m_min,sunrise,sunset&timezone=auto&forecast_days=7");
            var dto = await Client.GetFromJsonAsync<ForecastDto>(url, token) ?? throw new InvalidDataException("Weather response was empty.");
            var dayCount = new[] { dto.Daily.Time.Count, dto.Daily.Minimum.Count, dto.Daily.Maximum.Count, dto.Daily.WeatherCode.Count }.Min();
            var days = Enumerable.Range(0, dayCount).Select(index => new WeatherDay(
                DateOnly.Parse(dto.Daily.Time[index], CultureInfo.InvariantCulture), dto.Daily.Minimum[index], dto.Daily.Maximum[index], dto.Daily.WeatherCode[index])).ToList();
            var hourCount = new[] { dto.Hourly.Time.Count, dto.Hourly.Temperature.Count, dto.Hourly.WeatherCode.Count, dto.Hourly.PrecipitationProbability.Count }.Min();
            var hours = Enumerable.Range(0, hourCount).Select(index => new WeatherHour(
                DateTime.Parse(dto.Hourly.Time[index], CultureInfo.InvariantCulture).ToString("HH:mm", CultureInfo.InvariantCulture),
                dto.Hourly.Temperature[index], dto.Hourly.WeatherCode[index], dto.Hourly.PrecipitationProbability[index])).ToList();
            var value = new WeatherSnapshot(city, dto.Current.Temperature, dto.Current.Apparent, dto.Current.Humidity,
                dto.Current.Wind, dto.Current.Precipitation, dto.Current.WeatherCode, DateTimeOffset.Now, hours, days,
                dto.Daily.Sunrise.FirstOrDefault() ?? "", dto.Daily.Sunset.FirstOrDefault() ?? "");
            await store.SaveAsync("weather-cache.json", value, token);
            return value;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) when (cached is not null)
        {
            return cached with { IsStale = true };
        }
    }

    public async Task<IReadOnlyList<WeatherPlace>> SuggestCitiesAsync(string query, int count = 6, CancellationToken token = default)
    {
        query = query.Trim();
        if (query.Length < 2) return [];
        count = Math.Clamp(count, 1, 10);
        var url = $"https://geocoding-api.open-meteo.com/v1/search?count={count}&language=en&format=json&name=" + Uri.EscapeDataString(query);
        var dto = await Client.GetFromJsonAsync<GeocodingDto>(url, token);
        return dto?.Results?.Select(result => new WeatherPlace(result.Name, result.Admin1, result.Country,
            result.Latitude, result.Longitude, result.Timezone)).ToList() ?? [];
    }

    public async Task<WeatherPlace?> GeocodeAsync(string query, CancellationToken token = default)
    {
        var results = await SuggestCitiesAsync(query, 1, token);
        return results.Count > 0 ? results[0] : null;
    }

    private sealed class ForecastDto
    {
        [JsonPropertyName("current")] public CurrentDto Current { get; set; } = new();
        [JsonPropertyName("hourly")] public HourlyDto Hourly { get; set; } = new();
        [JsonPropertyName("daily")] public DailyDto Daily { get; set; } = new();
    }
    private sealed class CurrentDto
    {
        [JsonPropertyName("temperature_2m")] public double Temperature { get; set; }
        [JsonPropertyName("apparent_temperature")] public double Apparent { get; set; }
        [JsonPropertyName("relative_humidity_2m")] public int Humidity { get; set; }
        [JsonPropertyName("wind_speed_10m")] public double Wind { get; set; }
        [JsonPropertyName("precipitation")] public double Precipitation { get; set; }
        [JsonPropertyName("weather_code")] public int WeatherCode { get; set; }
    }
    private sealed class HourlyDto
    {
        [JsonPropertyName("time")] public List<string> Time { get; set; } = [];
        [JsonPropertyName("temperature_2m")] public List<double> Temperature { get; set; } = [];
        [JsonPropertyName("weather_code")] public List<int> WeatherCode { get; set; } = [];
        [JsonPropertyName("precipitation_probability")] public List<double> PrecipitationProbability { get; set; } = [];
    }
    private sealed class DailyDto
    {
        [JsonPropertyName("time")] public List<string> Time { get; set; } = [];
        [JsonPropertyName("temperature_2m_min")] public List<double> Minimum { get; set; } = [];
        [JsonPropertyName("temperature_2m_max")] public List<double> Maximum { get; set; } = [];
        [JsonPropertyName("weather_code")] public List<int> WeatherCode { get; set; } = [];
        [JsonPropertyName("sunrise")] public List<string> Sunrise { get; set; } = [];
        [JsonPropertyName("sunset")] public List<string> Sunset { get; set; } = [];
    }
    private sealed class GeocodingDto { [JsonPropertyName("results")] public List<PlaceDto>? Results { get; set; } }
    private sealed class PlaceDto
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("admin1")] public string Admin1 { get; set; } = "";
        [JsonPropertyName("country")] public string Country { get; set; } = "";
        [JsonPropertyName("latitude")] public double Latitude { get; set; }
        [JsonPropertyName("longitude")] public double Longitude { get; set; }
        [JsonPropertyName("timezone")] public string Timezone { get; set; } = "";
    }
}
