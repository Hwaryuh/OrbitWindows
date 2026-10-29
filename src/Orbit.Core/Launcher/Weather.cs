using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace Orbit.Core.Launcher;

/// <summary>WMO weather codes, grouped as Orbit shows them.</summary>
public enum WeatherKind { Clear, PartlyCloudy, Cloudy, Fog, Drizzle, Rain, Snow, Thunderstorm, Other }

public sealed record WeatherReport(string City, double Temperature, WeatherKind Kind, double High, double Low, DateTime At);

public enum WeatherStatus { Loading, Ready, Failed }

/// <summary>
/// "날씨", "seoul weather", "도쿄 기온" from open-meteo.com (free, no key; Orbit: SmartAnswers.swift,
/// <c>Weather</c>): the place goes to the geocoder, its coordinates to the forecast. Only a place
/// that stayed in the query for <see cref="Settle"/> is sent, so half-typed names never leave the
/// machine. Reports are kept 15 minutes per place.
/// </summary>
public sealed class WeatherService : IDisposable
{
    private static readonly string[] Cues = ["weather", "날씨", "기온"];
    private static readonly string[] Fillers = [" in ", "오늘", "내일"];
    private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(400);

    private readonly object _gate = new();
    private readonly Dictionary<string, WeatherReport> _reports = [];
    private readonly Dictionary<string, DateTime> _failed = [];
    private readonly HashSet<string> _loading = [];
    private readonly Timer _settle;
    private string? _pending;
    private DateTime _pendingSince;

    public WeatherService() => _settle = new Timer(_ => StartPending(), null, Timeout.Infinite, Timeout.Infinite);

    /// <summary>Raised on a background thread when a report arrived or a lookup failed.</summary>
    public event Action? Changed;

    /// <summary>The place a weather query asks about: a known city, the words typed, or here.</summary>
    public static string? Parse(string query)
    {
        var lower = query.ToLowerInvariant();
        if (!Cues.Any(lower.Contains)) return null;
        var place = lower;
        foreach (var word in Cues.Concat(Fillers)) place = place.Replace(word, " ");
        place = place.Trim();
        if (place.Length == 0) return WorldClock.Local;
        return WorldClock.Find(place)?.Name ?? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(place);
    }

    /// <summary>The report for a place, starting a lookup once the place has settled. A stale report is shown while it refreshes.</summary>
    public (WeatherStatus Status, WeatherReport? Report) Get(string place)
    {
        lock (_gate)
        {
            // The query moved on from a place still waiting to be sent: never send it.
            if (_pending is not null && _pending != place) _pending = null;
            _reports.TryGetValue(place, out var report);
            if (report is not null && DateTime.UtcNow - report.At < Fresh) return (WeatherStatus.Ready, report);
            bool failed = _failed.TryGetValue(place, out var at) && DateTime.UtcNow - at < RetryAfter;
            if (!failed && !_loading.Contains(place) && _pending != place)
            {
                _pending = place;
                _pendingSince = DateTime.UtcNow;
                _settle.Change(Settle, Timeout.InfiniteTimeSpan);
            }
            if (report is not null) return (WeatherStatus.Ready, report);
            return (failed ? WeatherStatus.Failed : WeatherStatus.Loading, null);
        }
    }

    /// <summary>The query no longer asks about the weather: send nothing for the place it named.</summary>
    public void Forget()
    {
        lock (_gate) _pending = null;
    }

    public void Dispose() => _settle.Dispose();

    private void StartPending()
    {
        string place;
        lock (_gate)
        {
            if (_pending is not { } pending) return;
            // A callback already queued when the place changed: wait out the new place's own pause.
            var waited = DateTime.UtcNow - _pendingSince;
            if (waited < Settle)
            {
                _settle.Change(Settle - waited, Timeout.InfiniteTimeSpan);
                return;
            }
            _pending = null;
            if (!_loading.Add(pending)) return;
            place = pending;
        }
        _ = Task.Run(() => Fetch(place));
    }

    private async Task Fetch(string place)
    {
        WeatherReport? report = null;
        try
        {
            var lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            var geo = await Web.Client.GetStringAsync(
                $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(place)}&count=1&language={lang}").ConfigureAwait(false);
            if (ParsePlace(geo) is { } found)
            {
                var (name, latitude, longitude) = found;
                var forecast = await Web.Client.GetStringAsync(string.Create(CultureInfo.InvariantCulture,
                    $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&current=temperature_2m,weather_code&daily=temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=1"))
                    .ConfigureAwait(false);
                report = ParseForecast(forecast, name ?? place, DateTime.UtcNow);
            }
            if (report is null) Log.Warn("Weather lookup found no report");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            Log.Warn($"Weather lookup failed: {e.GetType().Name}");
        }
        lock (_gate)
        {
            _loading.Remove(place);
            if (report is null) _failed[place] = DateTime.UtcNow;
            else
            {
                _reports[place] = report;
                _failed.Remove(place);
            }
        }
        Changed?.Invoke();
    }

    /// <summary>The geocoder's first result: its name and coordinates.</summary>
    internal static (string? Name, double Latitude, double Longitude)? ParsePlace(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
                return null;
            var first = results[0];
            if (!first.TryGetProperty("latitude", out var lat) || !first.TryGetProperty("longitude", out var lon)
                || lat.ValueKind != JsonValueKind.Number || lon.ValueKind != JsonValueKind.Number)
                return null;
            string? name = first.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            return (name, lat.GetDouble(), lon.GetDouble());
        }
        catch (Exception e) when (JsonErrors.IsUnreadable(e))
        {
            // Not JSON, or an object where a list was expected (or the other way round).
            return null;
        }
    }

    /// <summary>Current temperature and code, and today's high and low (the current temperature if those are missing).</summary>
    internal static WeatherReport? ParseForecast(string json, string city, DateTime at)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("current", out var current)
                || !current.TryGetProperty("temperature_2m", out var t) || t.ValueKind != JsonValueKind.Number
                || !current.TryGetProperty("weather_code", out var c) || c.ValueKind != JsonValueKind.Number)
                return null;
            double temperature = t.GetDouble();
            double First(string key) =>
                root.TryGetProperty("daily", out var daily) && daily.ValueKind == JsonValueKind.Object && daily.TryGetProperty(key, out var list)
                && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0 && list[0].ValueKind == JsonValueKind.Number
                    ? list[0].GetDouble()
                    : temperature;
            return new WeatherReport(city, temperature, Describe((int)c.GetDouble()), First("temperature_2m_max"), First("temperature_2m_min"), at);
        }
        catch (Exception e) when (JsonErrors.IsUnreadable(e))
        {
            return null;
        }
    }

    public static WeatherKind Describe(int code) => code switch
    {
        0 => WeatherKind.Clear,
        1 or 2 => WeatherKind.PartlyCloudy,
        3 => WeatherKind.Cloudy,
        45 or 48 => WeatherKind.Fog,
        >= 51 and <= 57 => WeatherKind.Drizzle,
        (>= 61 and <= 67) or (>= 80 and <= 82) => WeatherKind.Rain,
        (>= 71 and <= 77) or 85 or 86 => WeatherKind.Snow,
        >= 95 and <= 99 => WeatherKind.Thunderstorm,
        _ => WeatherKind.Other,
    };
}
