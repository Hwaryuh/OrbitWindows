using System.Text.Json;
using Orbit.Core.Actions;

namespace Orbit.Tests;

public class OrbitActionTests
{
    [Theory]
    // Orbit's raw values (Core/OrbitAction.swift), so ring.slots reads the same on both.
    [InlineData(OrbitAction.CaptureArea, "captureArea")]
    [InlineData(OrbitAction.CaptureText, "captureText")]
    [InlineData(OrbitAction.EmptyTrash, "emptyTrash")]
    [InlineData(OrbitAction.MediaPlayPause, "mediaPlayPause")]
    [InlineData(OrbitAction.KeepAwake, "keepAwake")]
    public void IdsAreOrbitsRawValues(OrbitAction action, string id)
    {
        Assert.Equal(id, action.Id());
        Assert.Equal(action, OrbitActions.FromId(id));
    }

    [Fact]
    public void EveryIdRoundTrips()
    {
        foreach (var action in OrbitActions.All) Assert.Equal(action, OrbitActions.FromId(action.Id()));
        Assert.Null(OrbitActions.FromId("winLeft")); // window snapping is not ported
    }

    [Fact]
    public void JsonUsesTheId()
    {
        var json = JsonSerializer.Serialize(new[] { OrbitAction.CaptureArea, OrbitAction.Clipboard });
        Assert.Equal("""["captureArea","clipboard"]""", json);
        Assert.Equal([OrbitAction.CaptureArea, OrbitAction.Clipboard], JsonSerializer.Deserialize<OrbitAction[]>(json));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<OrbitAction[]>("""["winLeft"]"""));
    }

    [Fact]
    public void RunnerReportsWhatIsMissing()
    {
        var runner = new ActionRunner();
        var ran = new List<OrbitAction>();
        var missing = new List<OrbitAction>();
        runner.Unavailable += missing.Add;
        runner.Register(OrbitAction.Settings, () => ran.Add(OrbitAction.Settings));
        runner.Perform(OrbitAction.Settings);
        runner.Perform(OrbitAction.Launcher);
        Assert.Equal([OrbitAction.Settings], ran);
        Assert.Equal([OrbitAction.Launcher], missing);
        Assert.True(runner.IsAvailable(OrbitAction.Settings));
        Assert.False(runner.IsAvailable(OrbitAction.Launcher));
    }

    [Fact]
    public void EveryActionHasAGroupAndKeywords()
    {
        foreach (var action in OrbitActions.All) Assert.False(string.IsNullOrWhiteSpace(action.Keywords()));
    }
}
