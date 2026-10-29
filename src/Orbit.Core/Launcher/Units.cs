namespace Orbit.Core.Launcher;

public enum Dimension { Length, Mass, Temperature, Volume, Speed, Area, Data, Duration, Energy }

/// <summary>A unit of a dimension. In the dimension's base unit a value is <c>value * Factor + Offset</c>.</summary>
public sealed record Unit(Dimension Dimension, string Symbol, double Factor, double Offset = 0)
{
    public double ToBase(double value) => value * Factor + Offset;
    public double FromBase(double value) => (value - Offset) / Factor;
}

/// <summary>A conversion asked for in the launcher: "10 km", "10 km to mi", "30평".</summary>
public sealed record UnitAnswer(double Value, Unit From, double Converted, Unit To);

/// <summary>
/// Unit names and the conversion each unit goes to when none is named (Orbit: SmartAnswers.swift,
/// <c>UnitTable</c>). .NET has no Measurement, so each unit is a factor to its dimension's base
/// (metre, kilogram, kelvin, litre, metres per second, square metre, byte, second, kilojoule).
/// Data units are decimal, as Foundation's are.
/// </summary>
public static class Units
{
    private static readonly Unit Kilometers = new(Dimension.Length, "km", 1000);
    private static readonly Unit Meters = new(Dimension.Length, "m", 1);
    private static readonly Unit Centimeters = new(Dimension.Length, "cm", 0.01);
    private static readonly Unit Millimeters = new(Dimension.Length, "mm", 0.001);
    private static readonly Unit Miles = new(Dimension.Length, "mi", 1609.344);
    private static readonly Unit Yards = new(Dimension.Length, "yd", 0.9144);
    private static readonly Unit Feet = new(Dimension.Length, "ft", 0.3048);
    private static readonly Unit Inches = new(Dimension.Length, "in", 0.0254);
    private static readonly Unit Kilograms = new(Dimension.Mass, "kg", 1);
    private static readonly Unit Grams = new(Dimension.Mass, "g", 0.001);
    private static readonly Unit Pounds = new(Dimension.Mass, "lb", 0.45359237);
    private static readonly Unit Ounces = new(Dimension.Mass, "oz", 0.028349523125);
    private static readonly Unit Tons = new(Dimension.Mass, "t", 1000);
    private static readonly Unit Celsius = new(Dimension.Temperature, "°C", 1, 273.15);
    private static readonly Unit Fahrenheit = new(Dimension.Temperature, "°F", 5.0 / 9, 273.15 - 32 * 5.0 / 9);
    private static readonly Unit Kelvin = new(Dimension.Temperature, "K", 1);
    private static readonly Unit Liters = new(Dimension.Volume, "L", 1);
    private static readonly Unit Milliliters = new(Dimension.Volume, "mL", 0.001);
    private static readonly Unit Gallons = new(Dimension.Volume, "gal", 3.785411784);
    private static readonly Unit Cups = new(Dimension.Volume, "cup", 0.24);
    private static readonly Unit FluidOunces = new(Dimension.Volume, "fl oz", 0.0295735295625);
    private static readonly Unit KilometersPerHour = new(Dimension.Speed, "km/h", 1 / 3.6);
    private static readonly Unit MilesPerHour = new(Dimension.Speed, "mph", 0.44704);
    private static readonly Unit MetersPerSecond = new(Dimension.Speed, "m/s", 1);
    private static readonly Unit SquareMeters = new(Dimension.Area, "m²", 1);
    private static readonly Unit SquareFeet = new(Dimension.Area, "ft²", 0.09290304);
    private static readonly Unit Acres = new(Dimension.Area, "ac", 4046.8564224);
    private static readonly Unit Hectares = new(Dimension.Area, "ha", 10_000);
    /// <summary>Korean 평 (3.3058 m²), common for floor area.</summary>
    private static readonly Unit Pyeong = new(Dimension.Area, "평", 3.305785);
    private static readonly Unit Bytes = new(Dimension.Data, "B", 1);
    private static readonly Unit Kilobytes = new(Dimension.Data, "kB", 1e3);
    private static readonly Unit Megabytes = new(Dimension.Data, "MB", 1e6);
    private static readonly Unit Gigabytes = new(Dimension.Data, "GB", 1e9);
    private static readonly Unit Terabytes = new(Dimension.Data, "TB", 1e12);
    private static readonly Unit Hours = new(Dimension.Duration, "hr", 3600);
    private static readonly Unit Minutes = new(Dimension.Duration, "min", 60);
    private static readonly Unit Seconds = new(Dimension.Duration, "s", 1);
    private static readonly Unit Kilocalories = new(Dimension.Energy, "kcal", 4.184);
    private static readonly Unit Kilojoules = new(Dimension.Energy, "kJ", 1);

    private static readonly Dictionary<string, Unit> Names = BuildNames();

    private static readonly Dictionary<Unit, Unit> Counterparts = new(ReferenceEqualityComparer.Instance)
    {
        [Kilometers] = Miles, [Miles] = Kilometers, [Meters] = Feet, [Feet] = Meters,
        [Centimeters] = Inches, [Inches] = Centimeters, [Millimeters] = Inches, [Yards] = Meters,
        [Kilograms] = Pounds, [Pounds] = Kilograms, [Grams] = Ounces, [Ounces] = Grams,
        [Celsius] = Fahrenheit, [Fahrenheit] = Celsius,
        [Liters] = Gallons, [Gallons] = Liters, [Milliliters] = FluidOunces,
        [KilometersPerHour] = MilesPerHour, [MilesPerHour] = KilometersPerHour,
        [SquareMeters] = Pyeong, [Pyeong] = SquareMeters,
        [Gigabytes] = Megabytes, [Megabytes] = Gigabytes, [Terabytes] = Gigabytes,
        [Hours] = Minutes, [Minutes] = Seconds,
        [Kilocalories] = Kilojoules,
    };

    private static Dictionary<string, Unit> BuildNames()
    {
        var names = new Dictionary<string, Unit>();
        void Add(Unit unit, string words)
        {
            foreach (var word in words.Split(' ')) names[word] = unit;
        }
        Add(Kilometers, "km kilometer kilometers kilometre 킬로미터 키로 킬로");
        Add(Meters, "m meter meters metre 미터");
        Add(Centimeters, "cm centimeter centimeters 센티 센티미터 센치");
        Add(Millimeters, "mm millimeter millimeters 밀리 밀리미터");
        Add(Miles, "mi mile miles 마일");
        Add(Yards, "yd yard yards 야드");
        Add(Feet, "ft foot feet ' 피트");
        Add(Inches, "in inch inches \" 인치");
        Add(Kilograms, "kg kilogram kilograms kilo kilos 킬로그램 키로그램");
        Add(Grams, "g gram grams 그램");
        Add(Pounds, "lb lbs pound pounds 파운드");
        Add(Ounces, "oz ounce ounces 온스");
        Add(Tons, "t ton tons tonne 톤");
        Add(Celsius, "c °c celsius 섭씨 도");
        Add(Fahrenheit, "f °f fahrenheit 화씨");
        Add(Kelvin, "k kelvin 켈빈");
        Add(Liters, "l liter liters litre 리터");
        Add(Milliliters, "ml milliliter milliliters 밀리리터");
        Add(Gallons, "gal gallon gallons 갤런");
        Add(Cups, "cup cups 컵");
        Add(FluidOunces, "floz");
        Add(KilometersPerHour, "kmh km/h kph 시속");
        Add(MilesPerHour, "mph");
        Add(MetersPerSecond, "m/s mps");
        Add(SquareMeters, "m2 m² sqm 제곱미터");
        Add(SquareFeet, "ft2 ft² sqft");
        Add(Acres, "acre acres 에이커");
        Add(Hectares, "ha hectare hectares 헥타르");
        Add(Pyeong, "평 pyeong");
        Add(Bytes, "b byte bytes 바이트");
        Add(Kilobytes, "kb kilobyte kilobytes");
        Add(Megabytes, "mb megabyte megabytes 메가");
        Add(Gigabytes, "gb gigabyte gigabytes 기가");
        Add(Terabytes, "tb terabyte terabytes 테라");
        Add(Hours, "h hr hrs hour hours 시간");
        Add(Minutes, "min mins minute minutes 분");
        Add(Seconds, "s sec secs second seconds 초");
        Add(Kilocalories, "kcal cal calorie calories 칼로리");
        Add(Kilojoules, "kj kilojoule kilojoules");
        return names;
    }

    /// <summary>The unit a word names, also with a Korean particle after it.</summary>
    public static Unit? Find(string word)
    {
        foreach (var candidate in QueryWords.Candidates(word))
            if (Names.TryGetValue(candidate.ToLowerInvariant(), out var unit)) return unit;
        return null;
    }

    /// <summary>The conversion a query asks for: to the named unit, or to the usual counterpart.</summary>
    public static UnitAnswer? Answer(string query)
    {
        if (QueryWords.Split(query) is not { } split || Find(split.From) is not { } from) return null;
        var (value, _, toWord) = split;
        Unit? to;
        if (toWord is not null)
        {
            to = Find(toWord);
            if (to is null || to.Dimension != from.Dimension) return null;
        }
        else if (!Counterparts.TryGetValue(from, out to))
        {
            return null;
        }
        if (ReferenceEquals(to, from)) return null;
        return new UnitAnswer(value, from, to.FromBase(from.ToBase(value)), to);
    }

    /// <summary>"6.2137 mi".</summary>
    public static string Format(double value, Unit unit) => $"{QueryWords.Format(value)} {unit.Symbol}";
}
