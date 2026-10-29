using System.Windows.Threading;

namespace Orbit.App;

/// <summary>Running something on the UI thread a moment from now.</summary>
internal static class Later
{
    public static void Run(TimeSpan delay, Action action)
    {
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
    }
}
