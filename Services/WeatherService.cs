using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;

namespace DynamicIsland.Services;

public sealed record WeatherForecastDay(
    string DayLabel,
    int MaxTempC,
    int MinTempC,
    string Glyph,
    string Condition);

public sealed record WeatherSnapshot(
    bool IsAvailable,
    int TempC,
    string Condition,
    string Glyph,
    string LocationName,
    IReadOnlyList<WeatherForecastDay> ForecastDays)
{
    public string BadgeText => IsAvailable ? $"{TempC}°C" : "—°C";
    public string SummaryLine => ForecastDays.Count > 0
        ? string.Join(" · ", ForecastDays.Take(3).Select(d => $"{d.DayLabel} {d.MaxTempC}°/{d.MinTempC}°"))
        : Condition;
}

/// <summary>
/// Lightweight background weather service powered by Open-Meteo (zero API key required),
/// cached for 20 minutes with automatic IP/timezone coordinate detection and deterministic WMO code mapping.
/// </summary>
internal sealed class WeatherService : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(20);
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(6) };

    private readonly Dispatcher _ui;
    private readonly Func<AppSettings>? _getSettings;
    private readonly DispatcherTimer _timer;
    private bool _disposed;
    private bool _refreshing;

    public WeatherSnapshot Current { get; private set; } = new(
        IsAvailable: false,
        TempC: 24,
        Condition: "Clear",
        Glyph: "\uE706",
        LocationName: "Local",
        ForecastDays: Array.Empty<WeatherForecastDay>());

    public event Action<WeatherSnapshot>? Updated;

    public WeatherService(Dispatcher ui, Func<AppSettings>? getSettings = null)
    {
        _ui = ui;
        _getSettings = getSettings;
        _timer = new DispatcherTimer(RefreshInterval, DispatcherPriority.Background, (_, _) => _ = RefreshAsync(), _ui);
    }

    public void Start()
    {
        if (_disposed) return;
        _timer.Start();
        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (_disposed || _refreshing) return;
        _refreshing = true;
        try
        {
            string? cityOverride = _getSettings?.Invoke()?.WeatherCityOverride;
            var (lat, lon, city) = await ResolveCoordinatesAsync(cityOverride).ConfigureAwait(false);

            string url = string.Create(
                CultureInfo.InvariantCulture,
                $"https://api.open-meteo.com/v1/forecast?latitude={lat:F4}&longitude={lon:F4}&current=temperature_2m,weather_code,is_day&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=4");

            string json = await SharedHttp.GetStringAsync(url).ConfigureAwait(false);
            var snapshot = ParseOpenMeteoForecastJson(json, city);
            if (snapshot.IsAvailable)
            {
                Current = snapshot;
                _ = _ui.InvokeAsync(() => Updated?.Invoke(snapshot));
            }
        }
        catch (Exception ex)
        {
            App.Log($"[WeatherService] Refresh skipped/offline: {ex.Message}");
        }
        finally
        {
            _refreshing = false;
        }
    }

    private static async Task<(double Lat, double Lon, string City)> ResolveCoordinatesAsync(string? cityOverride)
    {
        if (!string.IsNullOrWhiteSpace(cityOverride))
        {
            try
            {
                string geoUrl = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(cityOverride.Trim())}&count=1&language=en&format=json";
                string geoJson = await SharedHttp.GetStringAsync(geoUrl).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(geoJson);
                if (doc.RootElement.TryGetProperty("results", out var results) &&
                    results.ValueKind == JsonValueKind.Array &&
                    results.GetArrayLength() > 0)
                {
                    var first = results[0];
                    double lat = first.GetProperty("latitude").GetDouble();
                    double lon = first.GetProperty("longitude").GetDouble();
                    string name = first.TryGetProperty("name", out var n) ? n.GetString() ?? cityOverride : cityOverride;
                    return (lat, lon, name);
                }
            }
            catch
            {
                // Fall through to IP/timezone fallback
            }
        }

        try
        {
            string ipJson = await SharedHttp.GetStringAsync("https://ipwho.is/").ConfigureAwait(false);
            using var doc = JsonDocument.Parse(ipJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("latitude", out var latEl) && root.TryGetProperty("longitude", out var lonEl))
            {
                string city = root.TryGetProperty("city", out var cEl) ? cEl.GetString() ?? "Local" : "Local";
                return (latEl.GetDouble(), lonEl.GetDouble(), city);
            }
        }
        catch
        {
            // Fall back to timezone offset approximation
        }

        return FallbackCoordinatesFromTimezone(TimeZoneInfo.Local.BaseUtcOffset);
    }

    internal static (double Lat, double Lon, string City) FallbackCoordinatesFromTimezone(TimeSpan utcOffset)
    {
        double hours = utcOffset.TotalHours;
        return hours switch
        {
            >= 5.0 and <= 6.0 => (28.6139, 77.2090, "New Delhi"),
            >= 8.0 and <= 9.5 => (35.6762, 139.6503, "Tokyo"),
            >= 0.0 and < 3.0 => (51.5074, -0.1278, "London"),
            <= -4.0 and >= -6.0 => (40.7128, -74.0060, "New York"),
            < -6.0 => (37.7749, -122.4194, "San Francisco"),
            _ => (28.6139, 77.2090, "Local")
        };
    }

    /// <summary>
    /// Pure, deterministic parser for Open-Meteo v1 forecast JSON payloads.
    /// </summary>
    internal static WeatherSnapshot ParseOpenMeteoForecastJson(string json, string locationName = "Local")
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new WeatherSnapshot(false, 0, "Unavailable", "\uE753", locationName, Array.Empty<WeatherForecastDay>());
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("current", out var currentEl))
        {
            return new WeatherSnapshot(false, 0, "Unavailable", "\uE753", locationName, Array.Empty<WeatherForecastDay>());
        }

        double temp = currentEl.TryGetProperty("temperature_2m", out var tEl) ? tEl.GetDouble() : 20.0;
        int code = currentEl.TryGetProperty("weather_code", out var cEl) ? cEl.GetInt32() : 0;
        bool isDay = !currentEl.TryGetProperty("is_day", out var dEl) || dEl.GetInt32() != 0;

        var (glyph, condition) = MapWmoWeatherCode(code, isDay);

        var days = new List<WeatherForecastDay>();
        if (root.TryGetProperty("daily", out var dailyEl) &&
            dailyEl.TryGetProperty("time", out var times) &&
            dailyEl.TryGetProperty("temperature_2m_max", out var maxTemps) &&
            dailyEl.TryGetProperty("temperature_2m_min", out var minTemps) &&
            dailyEl.TryGetProperty("weather_code", out var codes))
        {
            int len = Math.Min(
                Math.Min(times.GetArrayLength(), maxTemps.GetArrayLength()),
                Math.Min(minTemps.GetArrayLength(), codes.GetArrayLength()));

            for (int i = 0; i < len; i++)
            {
                string rawDate = times[i].GetString() ?? "";
                string dayLabel = DateTime.TryParse(rawDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                    ? dt.ToString("ddd", CultureInfo.InvariantCulture)
                    : $"D{i + 1}";
                int maxT = (int)Math.Round(maxTemps[i].GetDouble());
                int minT = (int)Math.Round(minTemps[i].GetDouble());
                int dCode = codes[i].GetInt32();
                var (dGlyph, dCond) = MapWmoWeatherCode(dCode, isDay: true);
                days.Add(new WeatherForecastDay(dayLabel, maxT, minT, dGlyph, dCond));
            }
        }

        return new WeatherSnapshot(
            IsAvailable: true,
            TempC: (int)Math.Round(temp),
            Condition: condition,
            Glyph: glyph,
            LocationName: string.IsNullOrWhiteSpace(locationName) ? "Local" : locationName,
            ForecastDays: days);
    }

    internal static (string Glyph, string Condition) MapWmoWeatherCode(int wmoCode, bool isDay = true) => wmoCode switch
    {
        0 => (isDay ? "\uE706" : "\uE708", isDay ? "Clear Sky" : "Clear Night"),
        1 or 2 => ("\uE753", "Partly Cloudy"),
        3 => ("\uE753", "Overcast"),
        45 or 48 => ("\uE753", "Foggy"),
        51 or 53 or 55 or 56 or 57 => ("\uE9C4", "Drizzle"),
        61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => ("\uE9C4", "Rain"),
        71 or 73 or 75 or 77 or 85 or 86 => ("\uE9C8", "Snow"),
        95 or 96 or 99 => ("\uE9C6", "Thunderstorm"),
        _ => ("\uE706", "Fair")
    };

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
    }
}
