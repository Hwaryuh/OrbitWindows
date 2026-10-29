using System.Text.Json;

namespace Orbit.Core;

/// <summary>What reading JSON throws when the text is not JSON, has another shape, or holds a value of another type.</summary>
internal static class JsonErrors
{
    public static bool IsUnreadable(Exception e) => e is JsonException or InvalidOperationException or FormatException;
}
