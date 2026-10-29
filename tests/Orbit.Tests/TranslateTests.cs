using Orbit.Core.Translate;

namespace Orbit.Tests;

public class LanguagesTests
{
    [Theory]
    [InlineData("안녕하세요 반갑습니다", "ko")]
    [InlineData("こんにちは", "ja")]
    [InlineData("我们今天去公园", "zh-CN")]
    [InlineData("我們今天去公園說話", "zh-TW")]
    [InlineData("Hello there, friend", "en")]
    [InlineData("12345 !!!", null)]
    public void GuessesByScript(string text, string? expected) => Assert.Equal(expected, Languages.Guess(text));

    [Fact]
    public void DirectionGoesToThePrimaryUnlessAlreadyInIt()
    {
        Assert.Equal("ko", Languages.Direction("Good morning", "ko", "en"));
        Assert.Equal("en", Languages.Direction("좋은 아침입니다", "ko", "en"));
        Assert.Equal("ko", Languages.Direction("123", "ko", "en"));
        Assert.Equal("en", Languages.Direction("我们走吧", "zh-CN", "en"));
    }

    [Fact]
    public void ScriptOnlyNamesCjk()
    {
        Assert.Equal("ja", Languages.Script("ありがとう"));
        Assert.Null(Languages.Script("Thank you"));
    }

    [Fact]
    public void NamesAndSameLanguage()
    {
        Assert.Equal("中文 (繁體)", Languages.Named("zh-tw"));
        Assert.Equal("Português", Languages.Named("pt-br"));
        Assert.Equal("XX", Languages.Named("xx"));
        Assert.True(Languages.Same("zh-CN", "zh-tw"));
        Assert.False(Languages.Same("ko", "ja"));
    }
}

public class TranslatorTests
{
    [Fact]
    public void GoogleAnswersAreJoined()
    {
        var r = Translator.ParseGoogle("""{"sentences":[{"trans":"안녕. ","orig":"Hi. "},{"trans":"잘 지내?"}],"src":"en"}""")!;
        Assert.Equal("안녕. 잘 지내?", r.Text);
        Assert.Equal("en", r.DetectedSource);
        Assert.Null(Translator.ParseGoogle("<html>"));
        Assert.Null(Translator.ParseGoogle("""{"error":1}"""));
    }

    [Fact]
    public void DeepLAnswersAndCodes()
    {
        var r = Translator.ParseDeepL("""{"translations":[{"detected_source_language":"EN","text":"안녕"}]}""")!;
        Assert.Equal(("안녕", "en"), (r.Text, r.DetectedSource));
        Assert.Null(Translator.ParseDeepL("""{"translations":[]}"""));
        Assert.Equal("EN-US", Translator.DeepLCode("en", asTarget: true));
        Assert.Equal("EN", Translator.DeepLCode("en", asTarget: false));
        Assert.Equal("ZH-HANT", Translator.DeepLCode("zh-TW", asTarget: true));
        Assert.Equal("KO", Translator.DeepLCode("ko", asTarget: true));
        Assert.Equal("api-free.deepl.com", Translator.DeepLHost("abc:fx").Host);
        Assert.Equal("api.deepl.com", Translator.DeepLHost("abc").Host);
    }

    [Fact]
    public void GoogleUriCarriesTheLanguages()
    {
        var uri = Translator.GoogleUri("ko", null).ToString();
        Assert.Contains("client=gtx", uri);
        Assert.Contains("sl=auto", uri);
        Assert.Contains("tl=ko", uri);
    }

    [Fact]
    public async Task FailuresAreNamedWithoutTheText()
    {
        var translator = new Translator(new HttpClient(new Fixed(System.Net.HttpStatusCode.Forbidden)));
        var e = await Assert.ThrowsAsync<TranslationException>(() => translator.TranslateAsync("secret words", "ko", null, TranslationEngine.DeepL, Formality.Auto, "k", default));
        Assert.Equal(TranslationFailure.BadKey, e.Failure);
        Assert.DoesNotContain("secret", e.Message);
        var noKey = await Assert.ThrowsAsync<TranslationException>(() => translator.TranslateAsync("x", "ko", null, TranslationEngine.DeepL, Formality.Auto, " ", default));
        Assert.Equal(TranslationFailure.NeedsKey, noKey.Failure);
        var limited = new Translator(new HttpClient(new Fixed(System.Net.HttpStatusCode.TooManyRequests)));
        Assert.Equal(TranslationFailure.RateLimited, (await Assert.ThrowsAsync<TranslationException>(() => limited.TranslateAsync("x", "ko", null, TranslationEngine.Google, Formality.Auto, "", default))).Failure);
    }

    private sealed class Fixed(System.Net.HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }
}

public class DoubleCopyTests
{
    private static DoubleCopy Detector() => new(() => 0.4);

    [Fact]
    public void TwoPressesThenAChangeFire()
    {
        var d = Detector();
        Assert.False(d.CopyPressed(1000));
        Assert.False(d.ClipboardChanged(1050));
        Assert.False(d.CopyPressed(1300));
        Assert.True(d.ClipboardChanged(1350));
        // Once per run.
        Assert.False(d.ClipboardChanged(1360));
    }

    [Fact]
    public void AChangeSeenBeforeItsPressStillPairs()
    {
        var d = Detector();
        d.CopyPressed(1000);
        Assert.False(d.ClipboardChanged(1350));
        Assert.True(d.CopyPressed(1300));
    }

    [Fact]
    public void NoChangeAfterTheSecondPressDoesNothing()
    {
        var d = Detector();
        d.CopyPressed(1000);
        d.ClipboardChanged(1050);
        Assert.False(d.CopyPressed(1300));
        // Far too late.
        Assert.False(d.ClipboardChanged(2000));
    }

    [Fact]
    public void PressesTooFarApartStartOver()
    {
        var d = Detector();
        d.CopyPressed(1000);
        d.CopyPressed(1500);
        Assert.False(d.ClipboardChanged(1550));
        Assert.False(d.CopyPressed(1700));
        Assert.True(d.ClipboardChanged(1750));
    }

    [Fact]
    public void AThirdPressPairsWithTheSecond()
    {
        var d = Detector();
        d.CopyPressed(1000);
        d.CopyPressed(1300);
        d.CopyPressed(1600);
        Assert.True(d.ClipboardChanged(1650));
    }
}

public class SecretsTests
{
    [Fact]
    public void RoundTripsAndBadDataReadsEmpty()
    {
        var stored = Secrets.Protect("deepl-key:fx");
        Assert.NotEqual("deepl-key:fx", stored);
        Assert.Equal("deepl-key:fx", Secrets.Unprotect(stored));
        Assert.Equal("", Secrets.Unprotect("not base64!"));
        Assert.Equal("", Secrets.Unprotect(Convert.ToBase64String([1, 2, 3])));
        Assert.Equal("", Secrets.Protect(""));
    }
}
