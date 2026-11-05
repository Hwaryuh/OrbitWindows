using Orbit.Core.Launcher;
using Orbit.Core.Settings;

namespace Orbit.Tests;

public class FuzzyTests
{
    [Theory]
    [InlineData("calculator", "Calculator", 1000)]
    [InlineData("cal", "Calculator", 900 - 10)]
    [InlineData("code", "Visual Studio Code", 750 - 18)]
    [InlineData("vsc", "Visual Studio Code", 700)]
    [InlineData("udio", "Visual Studio Code", 600 - 18)]
    [InlineData("vsco", "Visual Studio Code", 200 - 18)]
    public void ScoresRankAsOrbits(string query, string title, int score)
    {
        Assert.Equal(score, Fuzzy.Score(query, title));
    }

    [Fact]
    public void KeywordsCountOnlyAsPrefixes()
    {
        Assert.Equal(400, Fuzzy.Score("스크린", "영역 캡처", "screenshot capture 스크린샷"));
        Assert.Null(Fuzzy.Score("zzz", "영역 캡처", "screenshot"));
    }

    [Fact]
    public void KoreanMatchesBySubstringAndPrefix()
    {
        Assert.Equal(900 - 7, Fuzzy.Score("클립", "클립보드 기록"));
        Assert.NotNull(Fuzzy.Score("캡처", "영역 캡처"));
    }

    [Fact]
    public void EmptyQueryMatchesNothing() => Assert.Null(Fuzzy.Score("", "Anything"));

    [Theory]
    [InlineData(900, 500)]
    [InlineData(600, 500)]
    [InlineData(450, 400)]
    [InlineData(200, 0)]
    public void FloorDropsNoiseOnceNamesMatch(int best, int floor) => Assert.Equal(floor, Fuzzy.Floor(best));
}

public class CalculatorTests
{
    [Theory]
    [InlineData("12*8", 96)]
    [InlineData("1+2*3", 7)]
    [InlineData("(1+2)*3", 9)]
    [InlineData("2^3^2", 512)]
    [InlineData("-2^2", 4)]
    [InlineData("10%4", 2)]
    [InlineData("7×6", 42)]
    [InlineData("9÷2", 4.5)]
    [InlineData("1.5 + 1.5", 3)]
    public void Evaluates(string input, double value) => Assert.Equal(value, Calculator.Evaluate(input));

    [Theory]
    [InlineData("12")]          // no operator
    [InlineData("1+")]          // incomplete
    [InlineData("1/0")]         // not finite
    [InlineData("hello+1")]     // not arithmetic
    [InlineData("(1+2")]        // unbalanced
    [InlineData("2^999999")]    // overflows
    public void RejectsWhatIsNotArithmetic(string input) => Assert.Null(Calculator.Evaluate(input));

    [Fact]
    public void DeepNestingIsRefused()
    {
        Assert.Null(Calculator.Evaluate(new string('(', 60) + "1+1" + new string(')', 60)));
        Assert.Null(Calculator.Evaluate("1+" + new string('-', 300) + "1"));
    }
}

public sealed class FrecencyTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "orbit-frecency-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose() => File.Delete(_file);

    [Fact]
    public void EachUseAddsFifteenUpToOneFifty()
    {
        using var prefs = new Preferences(_file, new Dictionary<string, object?>());
        var frecency = new Frecency(prefs);
        Assert.Equal(0, frecency.Boost("app.x"));
        frecency.Bump("app.x");
        frecency.Bump("app.x");
        Assert.Equal(30, frecency.Boost("app.x"));
        for (int i = 0; i < 20; i++) frecency.Bump("app.x");
        Assert.Equal(150, frecency.Boost("app.x"));
    }

    [Fact]
    public void ClearingForgetsEveryUse()
    {
        using var prefs = new Preferences(_file, new Dictionary<string, object?>());
        var frecency = new Frecency(prefs);
        Assert.False(frecency.HasHistory);
        frecency.Bump("app.x");
        frecency.Bump("app.y");
        Assert.True(frecency.HasHistory);
        frecency.Clear();
        Assert.False(frecency.HasHistory);
        Assert.Equal(0, frecency.Boost("app.x"));
        frecency.Bump("app.x");
        Assert.Equal(15, frecency.Boost("app.x"));
    }
}
