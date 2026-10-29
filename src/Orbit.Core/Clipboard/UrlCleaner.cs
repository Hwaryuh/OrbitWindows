namespace Orbit.Core.Clipboard;

/// <summary>A link with its tracking parameters taken out, and the names that went.</summary>
public sealed record CleanedUrl(string Url, IReadOnlyList<string> Removed);

/// <summary>A tracking name that is data on other sites, so only removed on these hosts and their subdomains.</summary>
public sealed record HostRule(string Name, IReadOnlyList<string> Hosts);

/// <summary>
/// Takes tracking parameters out of a copied link (docs/plan/03-milestones.md, Clean URL rules;
/// behavior after Vorssaint, the lists are Orbit's own). Only a clipboard that is one http(s) link
/// and nothing else is touched. Names are matched percent-decoded and case-insensitively; every
/// other parameter keeps its order and spelling, and the fragment stays.
/// </summary>
public sealed class UrlCleaner
{
    /// <summary>Removed by prefix, everywhere.</summary>
    public static readonly IReadOnlyList<string> BuiltInPrefixes = ["utm_"];

    /// <summary>Removed everywhere.</summary>
    public static readonly IReadOnlyList<string> BuiltInNames =
    [
        "fbclid", "gclid", "gclsrc", "dclid", "gbraid", "wbraid", "msclkid", "yclid", "twclid", "ttclid", "li_fat_id",
        "igshid", "mc_cid", "mc_eid", "_hsenc", "_hsmi", "mkt_tok", "vero_id", "vero_conv", "oly_anon_id", "oly_enc_id",
        "rb_clickid", "s_cid", "srsltid", "_gl", "ref_src", "wickedid", "epik", "__twitter_impression",
    ];

    /// <summary>Removed only on these sites: elsewhere the same names carry real data.</summary>
    public static readonly IReadOnlyList<HostRule> BuiltInHostRules =
    [
        new("si", ["youtube.com", "youtu.be", "spotify.com"]),
        new("feature", ["youtube.com"]),
        new("pp", ["youtube.com"]),
        new("t", ["x.com", "twitter.com"]),
        new("s", ["x.com", "twitter.com"]),
        new("igsh", ["instagram.com"]),
        new("trk", ["linkedin.com"]),
        new("trackingId", ["linkedin.com"]),
        new("share_id", ["reddit.com"]),
        new("is_from_webapp", ["tiktok.com"]),
        new("sender_device", ["tiktok.com"]),
    ];

    private readonly HashSet<string> _names;
    private readonly IReadOnlyList<HostRule> _hostRules;

    /// <param name="added">Names the user added; removed everywhere.</param>
    /// <param name="disabled">Built-in names (global or per-site) the user switched off.</param>
    public UrlCleaner(IEnumerable<string>? added = null, IEnumerable<string>? disabled = null)
    {
        var off = new HashSet<string>(disabled ?? [], StringComparer.OrdinalIgnoreCase);
        _names = new HashSet<string>(BuiltInNames.Where(n => !off.Contains(n)), StringComparer.OrdinalIgnoreCase);
        foreach (var name in added ?? []) if (name.Trim().Length > 0) _names.Add(name.Trim());
        _hostRules = BuiltInHostRules.Where(r => !off.Contains(r.Name)).ToList();
        PrefixesOn = !off.Contains("utm_*");
    }

    private bool PrefixesOn { get; }

    /// <summary>
    /// The cleaned link when <paramref name="text"/> is one http(s) link with something to remove;
    /// null otherwise (not a lone link, or nothing to take out).
    /// </summary>
    public CleanedUrl? Clean(string text)
    {
        var link = text.Trim();
        if (link.Length == 0 || link.Any(char.IsWhiteSpace)) return null;
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || uri.Host.Length == 0)
            return null;

        // A ? inside the fragment (#section?x) is not a query.
        int hash = link.IndexOf('#');
        int queryStart = link.IndexOf('?');
        if (queryStart < 0 || (hash >= 0 && hash < queryStart)) return null;
        int fragmentStart = hash;
        string query = fragmentStart < 0 ? link[(queryStart + 1)..] : link[(queryStart + 1)..fragmentStart];
        string fragment = fragmentStart < 0 ? "" : link[fragmentStart..];

        var host = uri.Host.ToLowerInvariant();
        var kept = new List<string>();
        var removed = new List<string>();
        foreach (var pair in query.Split('&'))
        {
            if (pair.Length == 0) continue;
            int eq = pair.IndexOf('=');
            var name = Decode(eq < 0 ? pair : pair[..eq]);
            if (IsTracker(name, host)) removed.Add(name);
            else kept.Add(pair);
        }
        if (removed.Count == 0) return null;
        var cleaned = link[..queryStart] + (kept.Count > 0 ? "?" + string.Join("&", kept) : "") + fragment;
        return new CleanedUrl(cleaned, removed.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private bool IsTracker(string name, string host)
    {
        if (PrefixesOn && BuiltInPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) return true;
        if (_names.Contains(name)) return true;
        return _hostRules.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase) && r.Hosts.Any(h => OnHost(host, h)));
    }

    /// <summary>The host itself or one of its subdomains (www.youtube.com, m.youtube.com).</summary>
    public static bool OnHost(string host, string site) =>
        host == site || host.EndsWith("." + site, StringComparison.Ordinal);

    private static string Decode(string name)
    {
        try
        {
            return Uri.UnescapeDataString(name.Replace('+', ' '));
        }
        catch (UriFormatException)
        {
            return name;
        }
    }
}
