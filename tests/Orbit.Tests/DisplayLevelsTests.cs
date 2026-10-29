using Orbit.Core.Displays;

namespace Orbit.Tests;

public class DisplayLevelsTests
{
    [Fact]
    public void RawValuesFollowTheMonitorsMaximum()
    {
        Assert.Equal(50u, DisplayLevels.ToRaw(0.5, 100));
        Assert.Equal(128u, DisplayLevels.ToRaw(0.5, 255));
        Assert.Equal(100u, DisplayLevels.ToRaw(1.4, 100));
        Assert.Equal(0u, DisplayLevels.ToRaw(-1, 100));
        Assert.Equal(0.25, DisplayLevels.ToLevel(25, 100));
        Assert.Equal(1, DisplayLevels.ToLevel(120, 100));
        Assert.Null(DisplayLevels.ToLevel(5, 0));
    }

    [Fact]
    public void StepsSnapToSixteenths()
    {
        Assert.Equal(0.375, DisplayLevels.Stepped(0.30, up: true));
        Assert.Equal(0.25, DisplayLevels.Stepped(0.30, up: false));
        Assert.Equal(1, DisplayLevels.Stepped(0.99, up: true));
        Assert.Equal(0, DisplayLevels.Stepped(0.02, up: false));
        // Repeated presses add up from each new level.
        double level = 0.5;
        for (int i = 0; i < 3; i++) level = DisplayLevels.Stepped(level, up: true);
        Assert.Equal(0.6875, level);
    }

    [Fact]
    public void NearestSupportedPercentage()
    {
        byte[] levels = [0, 10, 20, 40, 60, 80, 100];
        Assert.Equal(40, DisplayLevels.Nearest(0.33, levels));
        Assert.Equal(100, DisplayLevels.Nearest(0.95, levels));
        Assert.Equal(37, DisplayLevels.Nearest(0.37, []));
    }

    [Theory]
    [InlineData(@"\\?\DISPLAY#BOE0867#4&1a2b3c&0&UID8388688#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}", @"DISPLAY\BOE0867\4&1a2b3c&0&UID8388688_0")]
    [InlineData(@"\\?\DISPLAY#SDC4189#5&abc&0&UID265988#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}", @"display\sdc4189\5&ABC&0&uid265988_12")]
    public void DevicePathsMatchWmiInstances(string path, string instance) =>
        Assert.Equal(DisplayLevels.HardwareKey(path), DisplayLevels.HardwareKey(instance));

    [Fact]
    public void DifferentPanelsDontMatch()
    {
        Assert.NotEqual(
            DisplayLevels.HardwareKey(@"\\?\DISPLAY#BOE0867#4&1a2b3c&0&UID8388688#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}"),
            DisplayLevels.HardwareKey(@"DISPLAY\BOE0867\4&1a2b3c&0&UID8388689_0"));
        // A screen without a device path keeps its GDI name, which has no instance suffix to drop.
        Assert.Equal(@"\\.\DISPLAY1", DisplayLevels.HardwareKey(@"\\.\DISPLAY1"));
    }

    [Fact]
    public void PresetsRoundTripAndSurviveBadJson()
    {
        var presets = new[] { new BrightnessPreset("Night", new() { ["a"] = 0.2, ["b"] = 0.1 }) };
        var read = DisplayLevels.ReadPresets(DisplayLevels.WritePresets(presets));
        Assert.Equal("Night", Assert.Single(read).Name);
        Assert.Equal(0.1, read[0].Levels["b"]);
        Assert.Empty(DisplayLevels.ReadPresets("{not json"));
        Assert.Empty(DisplayLevels.ReadPresets(""));
        Assert.Empty(DisplayLevels.ReadPresets("""[{"Name":"","Levels":{}}]"""));
    }
}
