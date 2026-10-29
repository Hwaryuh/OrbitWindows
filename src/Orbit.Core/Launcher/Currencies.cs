using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Orbit.Core.Settings;

namespace Orbit.Core.Launcher;

/// <summary>An amount to change from one currency to another.</summary>
public sealed record CurrencyQuery(double Value, string From, string To);

/// <summary>Currency names and codes in a query (Orbit: SmartAnswers.swift, <c>currency</c> and <c>Currency</c>).</summary>
public static class Currencies
{
    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["$"] = "USD", ["usd"] = "USD", ["dollar"] = "USD", ["dollars"] = "USD", ["달러"] = "USD", ["불"] = "USD", ["us$"] = "USD",
        ["krw"] = "KRW", ["won"] = "KRW", ["원"] = "KRW", ["₩"] = "KRW", ["원화"] = "KRW",
        ["jpy"] = "JPY", ["yen"] = "JPY", ["엔"] = "JPY", ["엔화"] = "JPY", ["円"] = "JPY",
        ["eur"] = "EUR", ["euro"] = "EUR", ["euros"] = "EUR", ["유로"] = "EUR", ["€"] = "EUR",
        ["gbp"] = "GBP", ["pound"] = "GBP", ["pounds"] = "GBP", ["파운드"] = "GBP", ["£"] = "GBP",
        ["cny"] = "CNY", ["rmb"] = "CNY", ["yuan"] = "CNY", ["위안"] = "CNY", ["元"] = "CNY",
        ["hkd"] = "HKD", ["홍콩달러"] = "HKD", ["twd"] = "TWD", ["대만달러"] = "TWD", ["sgd"] = "SGD", ["싱가포르달러"] = "SGD",
        ["aud"] = "AUD", ["호주달러"] = "AUD", ["cad"] = "CAD", ["캐나다달러"] = "CAD", ["chf"] = "CHF", ["프랑"] = "CHF",
        ["thb"] = "THB", ["바트"] = "THB", ["vnd"] = "VND", ["동"] = "VND", ["php"] = "PHP", ["페소"] = "PHP", ["inr"] = "INR", ["루피"] = "INR",
        ["idr"] = "IDR", ["루피아"] = "IDR", ["myr"] = "MYR", ["링깃"] = "MYR", ["nzd"] = "NZD", ["mxn"] = "MXN", ["brl"] = "BRL", ["rub"] = "RUB", ["루블"] = "RUB",
        ["btc"] = "BTC",
    };

    private static readonly (string Symbol, string Code)[] LeadingSymbols = [("$", "usd"), ("€", "eur"), ("£", "gbp"), ("¥", "jpy"), ("₩", "krw")];
    private static readonly Dictionary<string, string> DisplaySymbols = new() { ["USD"] = "$", ["EUR"] = "€", ["GBP"] = "£", ["JPY"] = "¥", ["KRW"] = "₩" };
    private static readonly HashSet<string> WholeUnits = ["KRW", "JPY", "VND", "IDR"];

    private static readonly Lazy<HashSet<string>> IsoCodes = new(() =>
        CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Select(c => { try { return new RegionInfo(c.Name).ISOCurrencySymbol; } catch (ArgumentException) { return null; } })
            .OfType<string>().ToHashSet());

    /// <summary>The currency of Windows' region, KRW if it has none.</summary>
    public static string Local
    {
        get
        {
            try { return RegionInfo.CurrentRegion.ISOCurrencySymbol; }
            catch (ArgumentException) { return "KRW"; }
        }
    }

    /// <summary>The ISO code a word names, also with a Korean particle after it.</summary>
    public static string? Code(string word)
    {
        foreach (var candidate in QueryWords.Candidates(word))
            if (Exact(candidate) is { } code) return code;
        return null;
    }

    private static string? Exact(string word)
    {
        var w = word.ToLowerInvariant().Trim().Trim(PunctuationOf(word));
        if (Aliases.TryGetValue(w, out var code)) return code;
        var upper = w.ToUpperInvariant();
        return w.Length == 3 && w.All(char.IsAsciiLetter) && IsoCodes.Value.Contains(upper) ? upper : null;
    }

    private static char[] PunctuationOf(string word) => word.Where(char.IsPunctuation).Distinct().ToArray();

    /// <summary>
    /// "100 usd", "$100 to won", "5만원을 달러로". Without a target: the local currency, or USD from it
    /// (EUR when local is USD).
    /// </summary>
    public static CurrencyQuery? Parse(string query, string local)
    {
        var text = query;
        foreach (var (symbol, code) in LeadingSymbols)
        {
            if (!text.StartsWith(symbol, StringComparison.Ordinal)) continue;
            var parts = text[symbol.Length..].Trim().Split(' ', 2);
            text = parts[0] + code + (parts.Length > 1 ? " " + parts[1] : "");
        }
        if (QueryWords.Split(text) is not { } split || Code(split.From) is not { } from) return null;
        string to;
        if (split.To is { } toWord)
        {
            if (Code(toWord) is not { } named) return null;
            to = named;
        }
        else
        {
            to = from == local ? (local == "USD" ? "EUR" : "USD") : local;
        }
        return from == to ? null : new CurrencyQuery(split.Value, from, to);
    }

    /// <summary>"₩1,234", "$12.34", "1,234.50 THB".</summary>
    public static string Format(double value, string code)
    {
        var number = value.ToString(WholeUnits.Contains(code) ? "#,0" : "#,0.00", CultureInfo.CurrentCulture);
        return DisplaySymbols.TryGetValue(code, out var symbol)
            ? (value < 0 ? "-" + symbol + number.TrimStart('-') : symbol + number)
            : $"{number} {code}";
    }
}

/// <summary>
/// Exchange rates per US dollar from open.er-api.com (free, no key, updated daily), refreshed when
/// six hours old and kept in the preferences, so a launcher opened offline still answers. The
/// request is a fixed URL: no amount or currency from the query leaves the machine.
/// </summary>
public sealed class ExchangeRates(Preferences preferences)
{
    public const string Key = "launcher.fxRates";
    private const string Url = "https://open.er-api.com/v6/latest/USD";
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Stored form of the rates: per 1 USD, when the service updated them and when Orbit fetched
    /// them (Unix seconds). The service updates once a day, so the six hours count from the fetch.
    /// </summary>
    public sealed record Snapshot(Dictionary<string, double> Rates, double Time, double Fetched = 0);

    private readonly object _gate = new();
    private Snapshot? _snapshot;
    private bool _loaded;
    private bool _loading;
    private DateTime _failedAt = DateTime.MinValue;

    /// <summary>Raised on a background thread when rates arrived or a refresh failed.</summary>
    public event Action? Changed;

    /// <summary>When the service last updated the rates in hand; null before any arrived.</summary>
    public DateTimeOffset? Updated
    {
        get
        {
            lock (_gate)
            {
                Load();
                return _snapshot is { } s ? DateTimeOffset.FromUnixTimeSeconds((long)s.Time) : null;
            }
        }
    }

    /// <summary>No rates in hand and the last attempt failed a moment ago.</summary>
    public bool Failed
    {
        get
        {
            lock (_gate) return _snapshot is null && DateTime.UtcNow - _failedAt < RetryAfter;
        }
    }

    /// <summary>Units of <paramref name="to"/> per one <paramref name="from"/>; null while none are in hand or a code is unknown.</summary>
    public double? Rate(string from, string to)
    {
        RefreshIfNeeded();
        lock (_gate) return _snapshot is { } s ? Cross(s.Rates, from, to) : null;
    }

    public bool HasRates
    {
        get
        {
            lock (_gate)
            {
                Load();
                return _snapshot is not null;
            }
        }
    }

    internal static double? Cross(IReadOnlyDictionary<string, double> perUsd, string from, string to)
    {
        double a = from == "USD" ? 1 : perUsd.GetValueOrDefault(from);
        double b = to == "USD" ? 1 : perUsd.GetValueOrDefault(to);
        return a > 0 && b > 0 ? b / a : null;
    }

    public void RefreshIfNeeded()
    {
        lock (_gate)
        {
            Load();
            if (_loading || DateTime.UtcNow - _failedAt < RetryAfter) return;
            if (_snapshot is { } s && DateTimeOffset.UtcNow.ToUnixTimeSeconds() - s.Fetched < MaxAge.TotalSeconds) return;
            _loading = true;
        }
        _ = Task.Run(Fetch);
    }

    private void Load()
    {
        if (_loaded) return;
        _loaded = true;
        var stored = preferences.Get<Snapshot?>(Key);
        if (stored is { Rates.Count: > 0 } && Plausible(stored.Time)) _snapshot = stored;
    }

    private async Task Fetch()
    {
        Snapshot? fetched = null;
        try
        {
            var json = await Web.Client.GetStringAsync(Url).ConfigureAwait(false);
            fetched = ParseRates(json);
            if (fetched is null) Log.Warn("Exchange rates answer could not be read");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            Log.Warn($"Exchange rates could not be loaded: {e.GetType().Name}");
        }
        lock (_gate)
        {
            _loading = false;
            if (fetched is null) _failedAt = DateTime.UtcNow;
            else _snapshot = fetched;
        }
        if (fetched is not null) preferences.Set(Key, fetched);
        Changed?.Invoke();
    }

    /// <summary>open.er-api.com's <c>rates</c> and <c>time_last_update_unix</c>; null if either is missing.</summary>
    internal static Snapshot? ParseRates(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("rates", out var rates) || rates.ValueKind != JsonValueKind.Object) return null;
            var table = new Dictionary<string, double>();
            foreach (var rate in rates.EnumerateObject())
                if (rate.Value.ValueKind == JsonValueKind.Number) table[rate.Name] = rate.Value.GetDouble();
            double now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            double time = root.TryGetProperty("time_last_update_unix", out var t) && t.ValueKind == JsonValueKind.Number && Plausible(t.GetDouble())
                ? t.GetDouble()
                : now;
            return table.Count > 0 ? new Snapshot(table, time, now) : null;
        }
        catch (Exception e) when (JsonErrors.IsUnreadable(e))
        {
            return null;
        }
    }

    /// <summary>A Unix time <see cref="DateTimeOffset.FromUnixTimeSeconds"/> accepts and that isn't in the future.</summary>
    private static bool Plausible(double seconds) => seconds > 0 && seconds <= DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 86_400;
}
