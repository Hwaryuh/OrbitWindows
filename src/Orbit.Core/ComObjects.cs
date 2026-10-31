using System.Runtime.InteropServices;

namespace Orbit.Core;

/// <summary>Letting go of COM objects now rather than whenever the garbage collector runs.</summary>
internal static class ComObjects
{
    /// <summary>Releases a runtime COM wrapper; null and plain .NET objects are left alone.</summary>
    public static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com);
    }
}
