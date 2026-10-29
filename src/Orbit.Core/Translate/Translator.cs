using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orbit.Core.Translate;

public enum TranslationEngine { Google, DeepL }

public enum Formality { Auto, Formal, Casual }

public enum TranslationFailure
{
    /// <summary>DeepL chosen with no key.</summary>
    NeedsKey,
    /// <summary>DeepL refused the key (403).</summary>
    BadKey,
    /// <summary>DeepL's monthly free characters are used up (456).</summary>
    Quota,
    /// <summary>Too many requests (429): wait a moment.</summary>
    RateLimited,
    /// <summary>No connection, or no answer in time.</summary>
    Offline,
    /// <summary>The service answered with something else.</summary>
    BadAnswer,
}

/// <summary>A translation that did not happen, with why. Its message is never the service's text.</summary>
public sealed class TranslationException(TranslationFailure failure, TranslationEngine engine) : Exception($"{engine} translation failed: {failure}")
{
    public TranslationFailure Failure { get; } = failure;
    public TranslationEngine Engine { get; } = engine;
}

public sealed record TranslationResult(string Text, string? DetectedSource, TranslationEngine Engine);

/// <summary>
/// Google (the public gtx endpoint Chrome's translate bar uses: unofficial, best effort) and DeepL
/// (the user's key; keys ending in ":fx" use the free host), as Orbit's TranslationEngine.swift.
/// Never logs the text.
/// </summary>
public sealed class Translator(HttpClient http)
{
    /// <summary>Longest text sent, as Orbit.</summary>
    public const int MaxLength = 5000;

    public async Task<TranslationResult> TranslateAsync(string text, string target, string? source, TranslationEngine engine,
        Formality formality, string deeplKey, CancellationToken cancel)
    {
        if (text.Length > MaxLength) text = text[..MaxLength];
        try
        {
            return engine == TranslationEngine.DeepL
                ? await DeepLAsync(text, target, source, formality, deeplKey, cancel)
                : await GoogleAsync(text, target, source, cancel);
        }
        catch (HttpRequestException)
        {
            throw new TranslationException(TranslationFailure.Offline, engine);
        }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested)
        {
            // The client's own timeout, not the caller cancelling.
            throw new TranslationException(TranslationFailure.Offline, engine);
        }
    }

    public static Uri GoogleUri(string target, string? source) =>
        new($"https://translate.googleapis.com/translate_a/single?client=gtx&sl={Uri.EscapeDataString(source ?? "auto")}&tl={Uri.EscapeDataString(target)}&dt=t&dj=1");

    private async Task<TranslationResult> GoogleAsync(string text, string target, string? source, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, GoogleUri(target, source))
        {
            Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("q", text)]),
        };
        using var response = await http.SendAsync(request, cancel);
        Check(response.StatusCode, TranslationEngine.Google);
        var body = await response.Content.ReadAsStringAsync(cancel);
        return ParseGoogle(body) ?? throw new TranslationException(TranslationFailure.BadAnswer, TranslationEngine.Google);
    }

    /// <summary>The joined sentences and the detected language from gtx's JSON (dj=1).</summary>
    public static TranslationResult? ParseGoogle(string body)
    {
        try
        {
            if (JsonNode.Parse(body) is not JsonObject json || json["sentences"] is not JsonArray sentences) return null;
            var text = string.Concat(sentences.Select(s => s?["trans"]?.GetValue<string>() ?? ""));
            return new TranslationResult(text, json["src"]?.GetValue<string>(), TranslationEngine.Google);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private async Task<TranslationResult> DeepLAsync(string text, string target, string? source, Formality formality, string key, CancellationToken cancel)
    {
        key = key.Trim();
        if (key.Length == 0) throw new TranslationException(TranslationFailure.NeedsKey, TranslationEngine.DeepL);
        var payload = new Dictionary<string, object> { ["text"] = new[] { text }, ["target_lang"] = DeepLCode(target, asTarget: true) };
        if (source is not null) payload["source_lang"] = DeepLCode(source, asTarget: false);
        if (formality != Formality.Auto) payload["formality"] = formality == Formality.Formal ? "prefer_more" : "prefer_less";
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(DeepLHost(key), "/v2/translate")) { Content = JsonContent.Create(payload) };
        request.Headers.TryAddWithoutValidation("Authorization", $"DeepL-Auth-Key {key}");
        using var response = await http.SendAsync(request, cancel);
        Check(response.StatusCode, TranslationEngine.DeepL);
        var body = await response.Content.ReadAsStringAsync(cancel);
        return ParseDeepL(body) ?? throw new TranslationException(TranslationFailure.BadAnswer, TranslationEngine.DeepL);
    }

    public static Uri DeepLHost(string key) => new(key.Trim().EndsWith(":fx", StringComparison.Ordinal) ? "https://api-free.deepl.com" : "https://api.deepl.com");

    public static TranslationResult? ParseDeepL(string body)
    {
        try
        {
            if (JsonNode.Parse(body)?["translations"] is not JsonArray { Count: > 0 } list || list[0] is not JsonObject first) return null;
            var text = first["text"]?.GetValue<string>();
            return text is null ? null : new TranslationResult(text, first["detected_source_language"]?.GetValue<string>()?.ToLowerInvariant(), TranslationEngine.DeepL);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>DeepL's codes: a target needs a variant for English and Portuguese, and Chinese by script.</summary>
    public static string DeepLCode(string code, bool asTarget) => code switch
    {
        "en" => asTarget ? "EN-US" : "EN",
        "pt" => asTarget ? "PT-BR" : "PT",
        "zh-CN" => asTarget ? "ZH-HANS" : "ZH",
        "zh-TW" => asTarget ? "ZH-HANT" : "ZH",
        _ => code.ToUpperInvariant(),
    };

    private static void Check(HttpStatusCode status, TranslationEngine engine)
    {
        if ((int)status is >= 200 and < 300) return;
        Log.Warn($"{engine} answered {(int)status}");
        throw new TranslationException(status switch
        {
            HttpStatusCode.Forbidden when engine == TranslationEngine.DeepL => TranslationFailure.BadKey,
            (HttpStatusCode)456 => TranslationFailure.Quota,
            HttpStatusCode.TooManyRequests => TranslationFailure.RateLimited,
            _ => TranslationFailure.BadAnswer,
        }, engine);
    }
}
