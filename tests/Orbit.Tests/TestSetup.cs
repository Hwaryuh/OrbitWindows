using System.Runtime.CompilerServices;
using Orbit.Core;

namespace Orbit.Tests;

internal static class TestSetup
{
    /// <summary>Before any test: Orbit's log goes to a temporary folder, not the user's.</summary>
    [ModuleInitializer]
    internal static void Initialize() => Log.UseFolder(Path.Combine(Path.GetTempPath(), "orbit-tests-log"));
}
