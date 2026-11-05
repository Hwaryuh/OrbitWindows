using Orbit.Core.Actions;
using Orbit.Core.Input;
using Orbit.Core.Overlaps;

namespace Orbit.Tests;

public class OverlapScanTests
{
    private const Modifiers CtrlAlt = Modifiers.Ctrl | Modifiers.Alt;
    private static readonly Hotkey AltSpace = new(Modifiers.Alt, 0x20), Quiet = new(CtrlAlt, 'X');
    private static readonly string Launcher = OrbitAction.Launcher.Id();

    private static OverlapInput On(string[]? running = null, string[]? installed = null, Hotkey? run = null) =>
        new(running ?? [], installed ?? [], run);

    private static Dictionary<string, Hotkey> Keys(string slot, Hotkey keys) => new() { [slot] = keys };

    [Fact]
    public void NothingFoundOnACleanPc() => Assert.Empty(OverlapScan.Find(On(), Keys("snippets", Quiet)));

    [Fact]
    public void PowerToysRunClashesOnlyWhenPowerToysIsHere()
    {
        Assert.DoesNotContain(OverlapScan.Find(On(), Keys(Launcher, AltSpace)), o => o.Program == "PowerToys Run");
        var found = Assert.Single(OverlapScan.Find(On(running: ["powertoys"]), Keys(Launcher, AltSpace)), o => o.Kind == OverlapKind.KeyClash);
        Assert.Equal("PowerToys Run", found.Program);
        Assert.Equal(Launcher, found.SlotKey);
        Assert.Equal(AltSpace, found.Keys);
    }

    [Fact]
    public void PowerToysRunMovedElsewhereIsNoClashAndItsNewKeysAre()
    {
        var moved = new Hotkey(Modifiers.Win, 'R');
        Assert.DoesNotContain(OverlapScan.Find(On(installed: ["PowerToys (Preview)"], run: moved), Keys(Launcher, AltSpace)), o => o.Kind == OverlapKind.KeyClash);
        Assert.Contains(OverlapScan.Find(On(installed: ["PowerToys (Preview)"], run: moved), Keys("snippets", moved)), o => o.Program == "PowerToys Run");
    }

    [Fact]
    public void FlowLauncherOnAltSpace() =>
        Assert.Contains(OverlapScan.Find(On(running: ["Flow.Launcher"]), Keys(Launcher, AltSpace)), o => o.Program == "Flow Launcher" && o.Kind == OverlapKind.KeyClash);

    [Fact]
    public void WordHeadingsThePassThroughHandlesAreLeftOut()
    {
        Assert.DoesNotContain(OverlapScan.Find(On(), Keys(OrbitAction.CaptureText.Id(), new Hotkey(CtrlAlt, '2'))), o => o.Program is "Word" or "OneNote");
        Assert.Contains(OverlapScan.Find(On(), Keys(OrbitAction.Translate.Id(), new Hotkey(CtrlAlt, 'T'))), o => o.Program == "Word");
    }

    [Fact]
    public void WindowsKeysShowWithoutAnyProgram() =>
        Assert.Contains(OverlapScan.Find(On(), Keys("snippets", new Hotkey(Modifiers.Win, 'V'))), o => o.Program == "Windows");

    [Theory]
    [InlineData("Ditto", OrbitAction.Clipboard)]
    [InlineData("ShareX", OrbitAction.CaptureArea)]
    [InlineData("Greenshot", OrbitAction.CaptureArea)]
    [InlineData("Twinkle Tray", OrbitAction.Displays)]
    public void AProgramDoingTheSameJobIsNamed(string program, OrbitAction job)
    {
        var found = Assert.Single(OverlapScan.Find(On(installed: [program + " 1.2"]), new Dictionary<string, Hotkey>()));
        Assert.Equal(OverlapKind.SameJob, found.Kind);
        Assert.Equal(job, found.Job);
    }

    [Fact]
    public void ANameMatchingNothingIsIgnored() => Assert.Empty(OverlapScan.Find(On(running: ["notepad"], installed: ["Notepad++"]), new Dictionary<string, Hotkey>()));

    [Fact]
    public void TheLauncherIsOfferedCtrlAltLFirst() => Assert.Equal(new Hotkey(CtrlAlt, 'L'), OverlapScan.Suggest(Launcher, [AltSpace]));

    [Fact]
    public void ATakenSuggestionIsSkipped()
    {
        var suggested = OverlapScan.Suggest(Launcher, [new Hotkey(CtrlAlt, 'L')]);
        Assert.NotNull(suggested);
        Assert.NotEqual(new Hotkey(CtrlAlt, 'L'), suggested);
        Assert.Empty(ShortcutRules.KnownOwners(suggested!.Value));
    }

    [Fact]
    public void NoSuggestionWhenAllAreTaken()
    {
        var all = "LJNOBGHIMQUWYZ".Select(c => new Hotkey(CtrlAlt, c));
        Assert.Null(OverlapScan.Suggest("snippets", all));
    }

    [Fact]
    public void PowerToysRunKeysAreRead()
    {
        const string json = """{"properties":{"open_powerlauncher":{"win":true,"ctrl":false,"alt":false,"shift":true,"code":82,"key":""}}}""";
        Assert.Equal(new Hotkey(Modifiers.Win | Modifiers.Shift, 82), OverlapScan.ParsePowerToysRun(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"properties":{}}""")]
    [InlineData("""{"properties":{"open_powerlauncher":{"alt":"yes","code":32}}}""")]
    [InlineData("""{"properties":{"open_powerlauncher":{"alt":true,"code":"x"}}}""")]
    [InlineData("""{"properties":{"open_powerlauncher":{"alt":true,"code":0}}}""")]
    [InlineData("""{"properties":{"open_powerlauncher":{"alt":true,"code":9999}}}""")]
    public void UnexpectedPowerToysFilesMeanNoData(string json) => Assert.Null(OverlapScan.ParsePowerToysRun(json));

}
