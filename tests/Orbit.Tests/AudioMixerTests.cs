using Orbit.Core.Audio;

namespace Orbit.Tests;

public class AudioMixerTests
{
    /// <summary>Reads only: the PC's volumes are never changed by the tests.</summary>
    [Fact]
    public void ListsSessionsAndTheMasterWithoutChangingThem()
    {
        var mixer = new AudioMixer();
        var apps = mixer.Apps();
        Assert.All(apps, a => Assert.InRange(a.Volume, 0f, 1f));
        Assert.All(apps, a => Assert.InRange(a.Peak, 0f, 1f));
        Assert.True(apps.Count(a => a.IsSystemSounds) <= 1);
        if (mixer.Master() is { } master) Assert.InRange(master.Volume, 0f, 1f);
        Assert.False(mixer.SetVolume("no-such-program.exe", 0.5f));
    }
}
