using System.Globalization;
using Orbit.Core.Launcher;

namespace Orbit.Tests;

public class QueryWordsTests
{
    [Theory]
    [InlineData("1,200", 1200)]
    [InlineData("3.5", 3.5)]
    [InlineData("5만", 50_000)]
    [InlineData("2천", 2000)]
    [InlineData("1.2억", 120_000_000)]
    [InlineData("10k", 10_000)]
    public void AmountsReadKoreanAndShortMagnitudes(string text, double value)
    {
        Assert.Equal(value, QueryWords.Amount(text)!.Value, 6);
    }

    [Fact]
    public void SplitFindsAmountUnitsAndJoiner()
    {
        Assert.Equal((10.0, "km", "mi"), QueryWords.Split("10 km to mi"));
        Assert.Equal((10.0, "km", "miles"), QueryWords.Split("10km in miles"));
        Assert.Equal((50_000.0, "원을", "달러로"), QueryWords.Split("5만원을 달러로"));
        Assert.Equal((3.0, "kg", (string?)null), QueryWords.Split("3 kg"));
    }

    [Theory]
    [InlineData("5")]
    [InlineData("km")]
    [InlineData("10 in")]
    [InlineData("10 a b c")]
    public void SplitRejectsWhatIsNotAConversion(string text)
    {
        Assert.Null(QueryWords.Split(text));
    }

    [Fact]
    public void CandidatesDropOneKoreanParticle()
    {
        Assert.Equal(["달러로", "달러"], QueryWords.Candidates("달러로"));
        Assert.Equal(["원을", "원"], QueryWords.Candidates("원을"));
        Assert.Equal(["로"], QueryWords.Candidates("로"));
    }
}

public class UnitsTests
{
    [Theory]
    [InlineData("10 km", 6.2137, "mi")]
    [InlineData("100 f to c", 37.7778, "°C")]
    [InlineData("0 c", 32, "°F")]
    [InlineData("300 k to c", 26.85, "°C")]
    [InlineData("30평", 99.1736, "m²")]
    [InlineData("84 m2", 25.4101, "평")]
    [InlineData("2 gb", 2000, "MB")]
    [InlineData("1 lb to g", 453.5924, "g")]
    [InlineData("1.5시간", 90, "min")]
    public void ConvertsToTheNamedOrUsualUnit(string query, double expected, string symbol)
    {
        var answer = Units.Answer(query)!;
        Assert.Equal(expected, answer.Converted, 3);
        Assert.Equal(symbol, answer.To.Symbol);
    }

    [Theory]
    [InlineData("10 km to kg")]
    [InlineData("10 km to km")]
    [InlineData("10 cup")]
    [InlineData("hello")]
    public void NoAnswerAcrossDimensionsOrWithoutACounterpart(string query)
    {
        Assert.Null(Units.Answer(query));
    }
}

public class CurrenciesTests
{
    [Theory]
    [InlineData("달러", "USD")]
    [InlineData("원을", "KRW")]
    [InlineData("eur", "EUR")]
    [InlineData("SEK", "SEK")]
    [InlineData("abc", null)]
    [InlineData("km", null)]
    public void CodesFromNamesParticlesAndIso(string word, string? code)
    {
        Assert.Equal(code, Currencies.Code(word));
    }

    [Fact]
    public void ParseDefaultsToLocalOrDollars()
    {
        Assert.Equal(new CurrencyQuery(100, "USD", "KRW"), Currencies.Parse("100 usd", "KRW"));
        Assert.Equal(new CurrencyQuery(50_000, "KRW", "USD"), Currencies.Parse("5만원", "KRW"));
        Assert.Equal(new CurrencyQuery(50_000, "KRW", "USD"), Currencies.Parse("5만원을 달러로", "KRW"));
        Assert.Equal(new CurrencyQuery(20, "USD", "EUR"), Currencies.Parse("$20", "USD"));
        Assert.Equal(new CurrencyQuery(5, "EUR", "JPY"), Currencies.Parse("€5 to yen", "KRW"));
    }

    [Theory]
    [InlineData("100 usd to km")]
    [InlineData("100 won", "KRW", true)]
    [InlineData("10 km")]
    public void ParseRejectsUnknownTargetsAndNonCurrencies(string query, string local = "KRW", bool sameAsLocalIsFine = false)
    {
        var parsed = Currencies.Parse(query, local);
        if (sameAsLocalIsFine) Assert.Equal("USD", parsed!.To);
        else Assert.Null(parsed);
    }

    [Fact]
    public void CrossRatesGoThroughTheDollar()
    {
        var perUsd = new Dictionary<string, double> { ["KRW"] = 1400, ["EUR"] = 0.9 };
        Assert.Equal(1400, ExchangeRates.Cross(perUsd, "USD", "KRW"));
        Assert.Equal(1 / 1400.0, ExchangeRates.Cross(perUsd, "KRW", "USD")!.Value, 12);
        Assert.Equal(1400 / 0.9, ExchangeRates.Cross(perUsd, "EUR", "KRW")!.Value, 9);
        Assert.Null(ExchangeRates.Cross(perUsd, "BTC", "KRW"));
    }

    [Fact]
    public void ParsesTheRatesAnswer()
    {
        var snapshot = ExchangeRates.ParseRates("""{"result":"success","time_last_update_unix":1761782400,"rates":{"USD":1,"KRW":1432.5,"EUR":0.86}}""")!;
        Assert.Equal(1432.5, snapshot.Rates["KRW"]);
        Assert.Equal(1761782400, snapshot.Time);
        Assert.Null(ExchangeRates.ParseRates("""{"result":"error"}"""));
        Assert.Null(ExchangeRates.ParseRates("not json"));
        Assert.Null(ExchangeRates.ParseRates("[1,2]"));
        // A time far off is replaced by the fetch time rather than trusted.
        var odd = ExchangeRates.ParseRates("""{"time_last_update_unix":1e30,"rates":{"KRW":1400}}""")!;
        Assert.Equal(odd.Fetched, odd.Time);
    }

    [Fact]
    public void FormatsWithSymbolsAndWholeUnits()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal("₩1,234,568", Currencies.Format(1234567.8, "KRW"));
            Assert.Equal("$12.30", Currencies.Format(12.3, "USD"));
            Assert.Equal("-$5.00", Currencies.Format(-5, "USD"));
            Assert.Equal("1,234.50 THB", Currencies.Format(1234.5, "THB"));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }
}

public class WorldClockTests
{
    [Theory]
    [InlineData("서울 시간", "Seoul")]
    [InlineData("time in tokyo", "Tokyo")]
    [InlineData("뉴욕 몇시", "New York")]
    [InlineData("new york time", "New York")]
    [InlineData("런던 지금 시간", "London")]
    public void FindsTheCityAskedAbout(string query, string city)
    {
        Assert.Equal(city, WorldClock.Parse(query)!.Name);
    }

    [Theory]
    [InlineData("3시간")]
    [InlineData("seoul")]
    [InlineData("time")]
    public void NeedsACueAndACity(string query)
    {
        Assert.Null(WorldClock.Parse(query));
    }

    [Fact]
    public void OffsetKeepsMinutes()
    {
        var delhi = WorldClock.Find("delhi")!.Zone;
        var now = DateTimeOffset.UtcNow;
        var offset = WorldClock.OffsetFromLocal(delhi, now);
        Assert.Equal(TimeSpan.FromMinutes(330) - TimeZoneInfo.Local.GetUtcOffset(now), offset);
    }
}

public class WeatherTests
{
    [Fact]
    public void ParsesThePlace()
    {
        Assert.Equal("Seoul", WeatherService.Parse("서울 날씨"));
        Assert.Equal("Tokyo", WeatherService.Parse("weather in tokyo"));
        Assert.Equal("Jeju", WeatherService.Parse("jeju 오늘 기온"));
        Assert.Equal(WorldClock.Local, WeatherService.Parse("날씨"));
        Assert.Null(WeatherService.Parse("seoul"));
    }

    [Fact]
    public void ReadsGeocoderAndForecast()
    {
        Assert.Equal(("서울", 37.566, 126.9784),
            WeatherService.ParsePlace("""{"results":[{"name":"서울","latitude":37.566,"longitude":126.9784}]}"""));
        Assert.Null(WeatherService.ParsePlace("""{"generationtime_ms":0.5}"""));

        var at = new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc);
        var report = WeatherService.ParseForecast(
            """{"current":{"temperature_2m":14.2,"weather_code":61},"daily":{"temperature_2m_max":[17.9],"temperature_2m_min":[9.1]}}""", "Seoul", at)!;
        Assert.Equal(new WeatherReport("Seoul", 14.2, WeatherKind.Rain, 17.9, 9.1, at), report);

        var noDaily = WeatherService.ParseForecast("""{"current":{"temperature_2m":3,"weather_code":0}}""", "Oslo", at)!;
        Assert.Equal((3.0, 3.0), (noDaily.High, noDaily.Low));
        Assert.Null(WeatherService.ParseForecast("""{"current":{}}""", "Oslo", at));
        Assert.Null(WeatherService.ParseForecast("""{"current":[1]}""", "Oslo", at));
        Assert.Null(WeatherService.ParsePlace("""{"results":[[]]}"""));
        Assert.Equal(3.0, WeatherService.ParseForecast("""{"current":{"temperature_2m":3,"weather_code":0},"daily":[]}""", "Oslo", at)?.High);
    }

    [Theory]
    [InlineData(0, WeatherKind.Clear)]
    [InlineData(2, WeatherKind.PartlyCloudy)]
    [InlineData(48, WeatherKind.Fog)]
    [InlineData(81, WeatherKind.Rain)]
    [InlineData(86, WeatherKind.Snow)]
    [InlineData(96, WeatherKind.Thunderstorm)]
    [InlineData(100, WeatherKind.Other)]
    public void MapsWmoCodes(int code, WeatherKind kind)
    {
        Assert.Equal(kind, WeatherService.Describe(code));
    }
}

public class FileSearchTests
{
    [Fact]
    public void SqlMatchesTheTermAsTyped()
    {
        var sql = FileSearch.Sql("it's 50%_[x]", @"C:\Users\o'neil");
        Assert.Contains(@"SCOPE='file:C:\Users\o''neil'", sql);
        Assert.Contains("LIKE '%it''s 50[%][_][[]x]%'", sql);
    }

    [Theory]
    [InlineData(@"C:\Users\me\AppData\Roaming\x.txt", true)]
    [InlineData(@"C:\Users\me\.git\config", true)]
    [InlineData(@"C:\Users\me\Documents\report.docx", false)]
    public void SkipsAppDataAndDotFolders(string path, bool hidden)
    {
        Assert.Equal(hidden, FileSearch.Hidden(path));
    }
}
