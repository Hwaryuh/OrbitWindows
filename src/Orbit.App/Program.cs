using Orbit.Core.Settings;
using Velopack;

namespace Orbit.App;

/// <summary>
/// The entry point. Velopack runs first: on install, update and uninstall it starts the app with
/// its own arguments, handles them here and exits before any window exists.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build()
            // A fresh install starts at sign-in, as the setting's default says; the user can turn it off.
            .OnFirstRun(_ => StartupRegistration.Apply(true))
            .OnBeforeUninstallFastCallback(_ => StartupRegistration.Apply(false))
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
