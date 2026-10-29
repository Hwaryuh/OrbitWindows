using System.Net.Http;

namespace Orbit.Core.Launcher;

/// <summary>
/// The one HTTP client for the launcher's answers. Errors are logged by type only: their messages
/// can carry the URL, and a URL can carry what someone typed (a city).
/// </summary>
internal static class Web
{
    public static readonly HttpClient Client = Create();

    private static HttpClient Create()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Orbit-for-Windows");
        return client;
    }
}
