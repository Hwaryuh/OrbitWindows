using System.Security.Cryptography;
using System.Text;

namespace Orbit.Core.Translate;

/// <summary>
/// Ctrl+C twice (docs/plan/03-milestones.md, Translate rules): fires only when the second press,
/// within the interval of the first, is followed within the interval by a clipboard change that
/// isn't Orbit's. Without a change nothing happens, so an older clipboard is never translated.
/// Times are milliseconds of one monotonic clock (Environment.TickCount64), stamped where each
/// event happened, so a change that reaches the UI thread before its key press still pairs.
/// </summary>
public sealed class DoubleCopy(Func<double> intervalSeconds)
{
    private long? _first;
    private long? _second;
    private long? _change;

    private long Window => (long)(intervalSeconds() * 1000);

    /// <summary>A fresh Ctrl+C down at <paramref name="at"/>. True when it completes a double press whose change already came.</summary>
    public bool CopyPressed(long at)
    {
        if (_second is { } second && at - second <= Window)
        {
            // A third press: the last two make the pair.
            _first = second;
            _second = at;
        }
        else if (_first is { } first && _second is null && at - first <= Window && at >= first)
        {
            _second = at;
        }
        else
        {
            _first = at;
            _second = null;
        }
        return TryFire();
    }

    /// <summary>A clipboard change (not Orbit's own) seen at <paramref name="at"/>. True when it completes a double press.</summary>
    public bool ClipboardChanged(long at)
    {
        _change = at;
        return TryFire();
    }

    private bool TryFire()
    {
        if (_second is not { } second || _change is not { } change) return false;
        // The change must come after the second press, and soon.
        if (change < second || change - second > Window) return false;
        _first = _second = _change = null;
        return true;
    }
}

/// <summary>Secrets (the DeepL key) kept in the settings encrypted for this Windows user (DPAPI).</summary>
public static class Secrets
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Orbit for Windows");

    public static string Protect(string secret) =>
        secret.Length == 0 ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), Entropy, DataProtectionScope.CurrentUser));

    /// <summary>The secret; empty when there is none or it can't be read (another user's, or damaged): the user enters it again.</summary>
    public static string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            Log.Warn("A stored key could not be read; it needs to be entered again");
            return "";
        }
    }
}
