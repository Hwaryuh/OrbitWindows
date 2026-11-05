using System.Runtime.InteropServices;
using Orbit.Core.Input;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Orbit.Core.Typing;

/// <summary>
/// Edits as key events. A character on a US keyboard goes as its key, so an app listening for keys
/// sees one. Anything else (Hangul) goes as a Unicode event per UTF-16 unit.
/// </summary>
public static class KeyOutput
{
    /// <summary>dwExtraInfo of these events: not Orbit's other marker, as the hook treats these apart.</summary>
    public const nuint Marker = 0x0B18;

    private const ushort VkBack = 0x08, VkLShift = 0xA0;
    private const ushort ScanBack = 0x0E, ScanLShift = 0x2A;
    private const string Shifted = "~!@#$%^&*()_+{}|:\"<>?";

    private readonly record struct UsKey(ushort Vk, ushort Scan, bool Shift);

    private static readonly Dictionary<char, UsKey> UsKeys = MakeUsKeys();

    /// <param name="edits">In order.</param>
    /// <param name="shiftHeld">Shift is down in Windows' view now. It is lifted where it would change a character, and put back.</param>
    /// <param name="capsLock">Caps Lock is on. It flips what Shift does to letters.</param>
    /// <param name="usKeys">The layout places characters as a US keyboard does. If not, everything goes as Unicode.</param>
    public static INPUT[] Build(IEnumerable<FieldEdit> edits, bool shiftHeld, bool capsLock, bool usKeys)
    {
        var events = new List<INPUT>();
        bool shift = shiftHeld;
        foreach (var edit in edits)
        {
            for (int i = 0; i < edit.Deletes; i++)
            {
                // Shift+Backspace is Backspace in most apps, but not in all.
                SetShift(events, ref shift, false);
                AddKey(events, VkBack, ScanBack);
            }
            foreach (char c in edit.Text)
            {
                if (usKeys && UsKeys.TryGetValue(c, out var key))
                {
                    bool wanted = char.IsAsciiLetter(c) ? key.Shift != capsLock : key.Shift;
                    SetShift(events, ref shift, wanted);
                    AddKey(events, key.Vk, key.Scan);
                }
                else
                {
                    events.Add(Event(0, c, KEYBD_EVENT_FLAGS.KEYEVENTF_UNICODE));
                    events.Add(Event(0, c, KEYBD_EVENT_FLAGS.KEYEVENTF_UNICODE | KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP));
                }
            }
        }
        SetShift(events, ref shift, shiftHeld);
        return [.. events];
    }

    /// <summary>The Windows SDK's size of one event, for SendInput.</summary>
    public static int EventSize => Marshal.SizeOf<INPUT>();

    private static void SetShift(List<INPUT> events, ref bool shift, bool wanted)
    {
        if (shift == wanted) return;
        events.Add(Event(VkLShift, ScanLShift, wanted ? 0 : KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP));
        shift = wanted;
    }

    private static void AddKey(List<INPUT> events, ushort vk, ushort scan)
    {
        events.Add(Event(vk, scan, 0));
        events.Add(Event(vk, scan, KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP));
    }

    private static INPUT Event(ushort vk, ushort scan, KEYBD_EVENT_FLAGS flags) => OrbitInput.Event(vk, scan, flags, Marker);

    private static Dictionary<char, UsKey> MakeUsKeys()
    {
        var keys = new Dictionary<char, UsKey> { [' '] = new UsKey(0x20, 0x39, false) };
        void Row(string plain, string shifted, ushort firstScan, params ushort[] vks)
        {
            for (int i = 0; i < plain.Length; i++)
            {
                ushort scan = (ushort)(firstScan + i);
                keys[plain[i]] = new UsKey(vks[i], scan, false);
                keys[shifted[i]] = new UsKey(vks[i], scan, true);
            }
        }
        static ushort[] Vks(string text) => [.. text.Select(c => (ushort)c)];

        Row("1234567890", "!@#$%^&*()", 0x02, Vks("1234567890"));
        Row("-=", "_+", 0x0C, 0xBD, 0xBB);
        Row("qwertyuiop", "QWERTYUIOP", 0x10, Vks("QWERTYUIOP"));
        Row("[]", "{}", 0x1A, 0xDB, 0xDD);
        Row("asdfghjkl", "ASDFGHJKL", 0x1E, Vks("ASDFGHJKL"));
        Row(";'`", ":\"~", 0x27, 0xBA, 0xDE, 0xC0);
        Row("\\", "|", 0x2B, 0xDC);
        Row("zxcvbnm", "ZXCVBNM", 0x2C, Vks("ZXCVBNM"));
        Row(",./", "<>?", 0x33, 0xBC, 0xBE, 0xBF);
        return keys;
    }
}
