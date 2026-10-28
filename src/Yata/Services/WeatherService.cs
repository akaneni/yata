using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Windows.Devices.Geolocation;
using Yata.Helpers;

namespace Yata.Services;

public enum WeatherIcon { Clear, ClearNight, PartlyCloudy, Cloudy, Fog, Rain, Storm, Snow }

/// <param name="RainAt">Start of the next hour likely to rain (within the alert window), if any.</param>
public sealed record WeatherReport(
    double Temperature, int Code, bool IsDay, double? High, double? Low,
    DateTime? RainAt, int RainChance, DateTime FetchedAt)
{
    public WeatherIcon Icon => Weather.IconFor(Code, IsDay);
    public string Description => Weather.Describe(Code);
    public string TemperatureText => Weather.Degrees(Temperature);
}

public sealed record WeatherPlace(string Name, string Detail, double Latitude, double Longitude);

public enum LocateResult { Found, Denied, Unavailable }

/// <summary>Open-Meteo responses and WMO weather codes. Pure, so it's testable.</summary>
internal static class Weather
{
    /// <summary>Chance of rain at which an hour counts as "likely to rain".</summary>
    public const int RainThreshold = 60;
    public static readonly TimeSpan RainWindow = TimeSpan.FromHours(2);

    public static WeatherReport? Parse(JsonElement root, DateTime now)
    {
        if (!root.TryGetProperty("current", out var current) ||
            !TryNumber(current, "temperature_2m", out double temperature) ||
            !TryNumber(current, "weather_code", out double code))
            return null;
        bool isDay = !TryNumber(current, "is_day", out double day) || day >= 1;

        double? high = null, low = null;
        if (root.TryGetProperty("daily", out var daily))
        {
            high = First(daily, "temperature_2m_max");
            low = First(daily, "temperature_2m_min");
        }

        var (rainAt, chance) = root.TryGetProperty("hourly", out var hourly) ? NextRain(hourly, now) : (null, 0);
        return new WeatherReport(temperature, (int)code, isDay, high, low, rainAt, chance, now);
    }

    /// <summary>The first hour in the coming <see cref="RainWindow"/> with a high chance of rain.</summary>
    private static (DateTime?, int) NextRain(JsonElement hourly, DateTime now)
    {
        if (!hourly.TryGetProperty("time", out var times) || !hourly.TryGetProperty("precipitation_probability", out var chances) ||
            times.ValueKind != JsonValueKind.Array || chances.ValueKind != JsonValueKind.Array)
            return (null, 0);

        var hour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
        int count = Math.Min(times.GetArrayLength(), chances.GetArrayLength());
        for (int i = 0; i < count; i++)
        {
            // timezone=auto: times are the place's local time, without an offset.
            if (!DateTime.TryParse(times[i].GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ||
                at < hour || at > now + RainWindow)
                continue;
            if (chances[i].ValueKind == JsonValueKind.Number && chances[i].GetInt32() >= RainThreshold)
                return (at, chances[i].GetInt32());
        }
        return (null, 0);
    }

    public static List<WeatherPlace> ParsePlaces(JsonElement root)
    {
        var places = new List<WeatherPlace>();
        if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            return places;
        foreach (var r in results.EnumerateArray())
        {
            if (!TryNumber(r, "latitude", out double lat) || !TryNumber(r, "longitude", out double lon) ||
                Str(r, "name") is not string name)
                continue;
            string detail = string.Join(", ", new[] { Str(r, "admin1"), Str(r, "country") }.Where(s => !string.IsNullOrEmpty(s)));
            places.Add(new WeatherPlace(name, detail, lat, lon));
        }
        return places;
    }

    /// <summary>
    /// Two decimals (~1 km) is plenty for a forecast and doesn't pin down a street. Also what the
    /// settings store, so the exact point from the search never leaves the PC more precise than this.
    /// </summary>
    public static double Coarse(double coordinate) => Math.Round(coordinate, 2);

    public static string Degrees(double celsius) => $"{Math.Round(celsius):0}°";

    public static WeatherIcon IconFor(int code, bool isDay) => code switch
    {
        0 or 1 => isDay ? WeatherIcon.Clear : WeatherIcon.ClearNight,
        2 => WeatherIcon.PartlyCloudy,
        3 => WeatherIcon.Cloudy,
        45 or 48 => WeatherIcon.Fog,
        >= 51 and <= 67 or >= 80 and <= 82 => WeatherIcon.Rain,
        >= 71 and <= 77 or 85 or 86 => WeatherIcon.Snow,
        >= 95 => WeatherIcon.Storm,
        _ => WeatherIcon.Cloudy,
    };

    /// <summary>WMO weather interpretation codes, as Open-Meteo documents them.</summary>
    public static string Describe(int code) => code switch
    {
        0 => Strings.Weather_Clear,
        1 => Strings.Weather_MostlyClear,
        2 => Strings.Weather_PartlyCloudy,
        3 => Strings.Weather_Cloudy,
        45 or 48 => Strings.Weather_Fog,
        51 or 53 or 55 => Strings.Weather_Drizzle,
        56 or 57 => Strings.Weather_FreezingDrizzle,
        61 => Strings.Weather_LightRain,
        63 => Strings.Weather_Rain,
        65 => Strings.Weather_HeavyRain,
        66 or 67 => Strings.Weather_FreezingRain,
        71 or 73 or 75 or 77 => Strings.Weather_Snow,
        80 or 81 => Strings.Weather_RainShowers,
        82 => Strings.Weather_HeavyShowers,
        85 or 86 => Strings.Weather_SnowShowers,
        95 => Strings.Weather_Thunderstorm,
        96 or 99 => Strings.Weather_ThunderstormHail,
        _ => "—",
    };

    /// <summary>It's raining (or snowing, or storming) right now, so a rain notice would be old news.</summary>
    public static bool IsWet(int code) => code >= 51;

    private static double? First(JsonElement e, string name) =>
        e.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array && array.GetArrayLength() > 0 &&
        array[0].ValueKind == JsonValueKind.Number ? array[0].GetDouble() : null;

    private static bool TryNumber(JsonElement e, string name, out double value)
    {
        value = 0;
        return e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out value);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>
/// Current weather and a short rain heads-up from Open-Meteo (https://open-meteo.com): free, open,
/// no account or key. Only the chosen city's coordinates, rounded to ~1 km, are sent.
/// Off until the user turns it on and picks a city.
/// </summary>
public sealed class WeatherService
{
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);
    // One rain notice per spell, not one per refresh.
    private static readonly TimeSpan RainNoticeGap = TimeSpan.FromHours(3);
    private static readonly HttpClient Http = HttpPool.Client(TimeSpan.FromSeconds(15));

    private readonly AppStore _store;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };
    private DateTime _nextFetch = DateTime.MinValue;
    private DateTime _lastRainNotice = DateTime.MinValue;
    private string? _fetchedFor;
    private bool _fetching;

    // Offline for this long: an old temperature is worse than none.
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(3);
    private WeatherReport? _report;

    /// <summary>Latest report, or null when off, without a city, not loaded yet or too old.</summary>
    public WeatherReport? Current => _report is { } r && DateTime.Now - r.FetchedAt < StaleAfter ? r : null;

    public event Action? Changed;
    public event Action<HudMessage>? Message;
    /// <summary>A forecast request started or ended.</summary>
    public event Action? BusyChanged;

    private readonly ManualGate _manual = new();

    /// <summary>When the forecast on screen was fetched, or null without one.</summary>
    public DateTime? LastFetched => _report?.FetchedAt;

    public bool IsFetching => _fetching;

    public bool IsActive => Settings.WeatherEnabled && Settings.WeatherLatitude.HasValue && Settings.WeatherLongitude.HasValue;

    /// <summary>"Refresh now" in the settings: fetches at once, unless it just did.</summary>
    public Task RefreshNowAsync()
    {
        if (!IsActive || _fetching || !_manual.TryPass())
            return Task.CompletedTask;
        _nextFetch = DateTime.MinValue;
        return RefreshIfDueAsync();
    }

    public WeatherService(AppStore store)
    {
        _store = store;
        _timer.Tick += (_, _) => _ = RefreshIfDueAsync();
    }

    private SettingsData Settings => _store.Settings;

    public void Start()
    {
        _timer.Start();
        _ = RefreshIfDueAsync();
    }

    public void Stop() => _timer.Stop();

    /// <summary>Turned on/off or a new city: drop the old report and fetch again.</summary>
    public void ApplySettings()
    {
        string? place = IsActive ? $"{Settings.WeatherLatitude}|{Settings.WeatherLongitude}" : null;
        if (place == _fetchedFor)
            return;
        _fetchedFor = place;
        _report = null;
        _nextFetch = DateTime.MinValue;
        Changed?.Invoke();
        _ = RefreshIfDueAsync();
    }

    /// <summary>City search for the settings window (Open-Meteo geocoding; sends only the typed name).</summary>
    public static async Task<List<WeatherPlace>> SearchAsync(string name, CancellationToken ct)
    {
        // Place and country names come back in the interface's language.
        string url = $"https://geocoding-api.open-meteo.com/v1/search?count=6&language={Lang.Culture.TwoLetterISOLanguageName}&format=json&name=" +
                     Uri.EscapeDataString(name.Trim());
        using var response = await Http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return Weather.ParsePlaces(json.RootElement);
    }

    /// <summary>
    /// One reading from Windows location, already rounded to ~1 km: the precise position never
    /// leaves this method. Asks for city-level accuracy so Windows doesn't need to wake the GPS.
    /// </summary>
    public static async Task<(LocateResult Result, double Latitude, double Longitude)> LocateOnceAsync()
    {
        try
        {
            if (await Geolocator.RequestAccessAsync() != GeolocationAccessStatus.Allowed)
                return (LocateResult.Denied, 0, 0);

            var locator = new Geolocator { DesiredAccuracyInMeters = 3000 };
            var position = await locator.GetGeopositionAsync(TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(15));
            var point = position.Coordinate.Point.Position;
            return (LocateResult.Found, Weather.Coarse(point.Latitude), Weather.Coarse(point.Longitude));
        }
        catch (UnauthorizedAccessException)
        {
            return (LocateResult.Denied, 0, 0);
        }
        catch (Exception ex) when (ex is TimeoutException or TaskCanceledException or System.Runtime.InteropServices.COMException)
        {
            // No Wi-Fi to locate from, timed out, or the location service is off.
            return (LocateResult.Unavailable, 0, 0);
        }
    }

    private async Task RefreshIfDueAsync()
    {
        if (!IsActive || _fetching || DateTime.Now < _nextFetch)
            return;

        _fetching = true;
        _nextFetch = DateTime.Now + RetryAfter;
        BusyChanged?.Invoke();
        string place = $"{Settings.WeatherLatitude}|{Settings.WeatherLongitude}";
        try
        {
            string url = "https://api.open-meteo.com/v1/forecast" +
                $"?latitude={Weather.Coarse(Settings.WeatherLatitude!.Value).ToString(CultureInfo.InvariantCulture)}" +
                $"&longitude={Weather.Coarse(Settings.WeatherLongitude!.Value).ToString(CultureInfo.InvariantCulture)}" +
                "&current=temperature_2m,weather_code,is_day&hourly=precipitation_probability" +
                "&daily=temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=2";
            using var response = await Http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            // Turned off or moved to another city while the request was out.
            if (!IsActive || place != $"{Settings.WeatherLatitude}|{Settings.WeatherLongitude}")
                return;

            _report = Weather.Parse(json.RootElement, DateTime.Now);
            _fetchedFor = place;
            _nextFetch = DateTime.Now + RefreshEvery;
            Changed?.Invoke();
            CheckRain();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Offline: keep the last report (it says when it was fetched) and retry soon.
            Log.Warn("weather", "Falha ao buscar o tempo", ex);
        }
        finally
        {
            _fetching = false;
            BusyChanged?.Invoke();
        }
    }

    private void CheckRain()
    {
        if (_report is not { RainAt: DateTime at } report || !Settings.WeatherRainAlerts || Weather.IsWet(report.Code) ||
            DateTime.Now - _lastRainNotice < RainNoticeGap)
            return;

        _lastRainNotice = DateTime.Now;
        string when = at <= DateTime.Now ? Strings.Weather_RainNow : string.Format(Lang.Culture, Strings.Weather_RainExpected, at);
        Message?.Invoke(new HudMessage(Glyphs.Cloud, when, $"{report.RainChance}%", DurationMs: 5000, Important: true,
            Kind: HudKind.Weather, Expires: DateTime.Now + TimeSpan.FromHours(2)));
    }
}
