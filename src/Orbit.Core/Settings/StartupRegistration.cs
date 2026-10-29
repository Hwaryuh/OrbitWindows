using Microsoft.Win32;

namespace Orbit.Core.Settings;

/// <summary>
/// Starting Orbit at sign-in: a value under HKCU\...\Run, for this user only (no admin). The path
/// is the running executable's, so a moved or updated copy writes its own when the setting is applied.
/// </summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Orbit";
    public const string StartupArgument = "--startup";

    /// <summary>
    /// Whether this copy is an installed one (Velopack puts Update.exe one folder above the app).
    /// A build run from the source tree must never write the Run value, nor overwrite an installed
    /// copy's.
    /// </summary>
    public static bool CanRegister(string? executable = null)
    {
        var path = executable ?? Environment.ProcessPath;
        var folder = string.IsNullOrEmpty(path) ? null : Path.GetDirectoryName(path);
        var parent = folder is null ? null : Path.GetDirectoryName(folder);
        return parent is not null && File.Exists(Path.Combine(parent, "Update.exe"));
    }

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    /// <summary>Writes or removes the Run value, for an installed copy only. Returns false if the registry refused.</summary>
    public static bool Apply(bool enabled, string? executable = null)
    {
        try
        {
            var path = executable ?? Environment.ProcessPath;
            if (string.IsNullOrEmpty(path)) return false;
            // Neither on nor off from a dev build: the Run value may be an installed copy's.
            if (!CanRegister(path))
            {
                Log.Info("Not an installed copy; the Run value is left alone");
                return true;
            }
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }
            key.SetValue(ValueName, $"\"{path}\" {StartupArgument}");
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Error("Sign-in start could not be changed", e);
            return false;
        }
    }
}
