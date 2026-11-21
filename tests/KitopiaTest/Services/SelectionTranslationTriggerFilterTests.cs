using System.Globalization;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Translation;
using PluginCore;
using PluginCore.Config;

namespace KitopiaTest.Services;

[TestClass]
public sealed class SelectionTranslationTriggerFilterTests
{
    [TestMethod]
    [DataRow("zh-CN", TranslationTargetLanguage.SimplifiedChinese)]
    [DataRow("zh-Hans", TranslationTargetLanguage.SimplifiedChinese)]
    [DataRow("zh-SG", TranslationTargetLanguage.SimplifiedChinese)]
    [DataRow("zh-TW", TranslationTargetLanguage.TraditionalChinese)]
    [DataRow("zh-HK", TranslationTargetLanguage.TraditionalChinese)]
    [DataRow("zh-MO", TranslationTargetLanguage.TraditionalChinese)]
    [DataRow("zh-Hant", TranslationTargetLanguage.TraditionalChinese)]
    [DataRow("en-US", TranslationTargetLanguage.English)]
    [DataRow("en-GB", TranslationTargetLanguage.English)]
    [DataRow("ja-JP", TranslationTargetLanguage.Japanese)]
    public void TryMapCulture_SupportedCulture_ReturnsExpectedTargetLanguage(
        string cultureName,
        TranslationTargetLanguage expected)
    {
        var ok = TranslationLanguageResolver.TryMapCulture(
            CultureInfo.GetCultureInfo(cultureName),
            out var actual);

        Assert.IsTrue(ok);
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    [DataRow("fr-FR")]
    [DataRow("de-DE")]
    [DataRow("ko-KR")]
    public void TryMapCulture_UnsupportedCulture_ReturnsFalse(string cultureName)
    {
        var ok = TranslationLanguageResolver.TryMapCulture(
            CultureInfo.GetCultureInfo(cultureName),
            out _);

        Assert.IsFalse(ok);
        Assert.AreEqual(
            TranslationTargetLanguage.SimplifiedChinese,
            TranslationLanguageResolver.ResolveTargetLanguageOrDefault(cultureName));
    }

    [TestMethod]
    [DoNotParallelize]
    public void TryResolveCurrentLanguage_ReadsConfigLanguageAndFallsBackOnInvalidCulture()
    {
        var originalConfigs = ConfigManger.Configs;
        try
        {
            ConfigManger.Configs = new Dictionary<string, ConfigBase>
            {
                ["KitopiaConfig"] = new KitopiaConfig { language = "en-US" }
            };

            Assert.IsTrue(TranslationLanguageResolver.TryResolveCurrentLanguage(
                null, CultureInfo.GetCultureInfo("zh-CN"), out var resolved));
            Assert.AreEqual(TranslationTargetLanguage.English, resolved);

            ConfigManger.Configs = new Dictionary<string, ConfigBase>
            {
                ["KitopiaConfig"] = new KitopiaConfig { language = "invalid-culture-tag" }
            };

            Assert.IsTrue(TranslationLanguageResolver.TryResolveCurrentLanguage(
                null, CultureInfo.GetCultureInfo("ja-JP"), out var fallbackResolved));
            Assert.AreEqual(TranslationTargetLanguage.Japanese, fallbackResolved);
        }
        finally
        {
            ConfigManger.Configs = originalConfigs;
        }
    }

    [TestMethod]
    [DataRow("你好世界", SelectionTranslationTriggerFilter.SkipReason.MatchesTargetLanguage)]
    [DataRow("客户端使用 Avalonia 实现自动划词翻译", SelectionTranslationTriggerFilter.SkipReason.MatchesTargetLanguage)]
    [DataRow("这个 PR 的 CI 挂了", SelectionTranslationTriggerFilter.SkipReason.MatchesTargetLanguage)]
    [DataRow("Windows 11 更新", SelectionTranslationTriggerFilter.SkipReason.MatchesTargetLanguage)]
    public void Evaluate_ChineseUiAndChineseTarget_SkipsChineseAndMixedChineseText(
        string text,
        SelectionTranslationTriggerFilter.SkipReason expectedReason)
    {
        var decision = SelectionTranslationTriggerFilter.Evaluate(
            text,
            1000,
            TranslationSourceLanguage.Auto,
            TranslationTargetLanguage.SimplifiedChinese,
            "zh-CN");

        Assert.IsFalse(decision.ShouldTrigger);
        Assert.AreEqual(expectedReason, decision.Reason);
    }

    [TestMethod]
    [DataRow("books")]
    [DataRow("The model supports")]
    [DataRow("well-known")]
    [DataRow("New York City")]
    [DataRow("Index-Translate-35B-A3B")]
    [DataRow("こんにちは")]
    [DataRow("日本の文化")]
    [DataRow("안녕하세요")]
    [DataRow("Привет мир")]
    [DataRow("facade")]
    [DataRow("coffee")]
    [DataRow("decade")]
    [DataRow("What's up? (2026)")]
    [DataRow("e.g. books")]
    [DataRow("U.S.")]
    [DataRow("and/or")]
    public void Evaluate_ChineseUiAndChineseTarget_AllowsForeignNaturalText(string text)
    {
        var decision = SelectionTranslationTriggerFilter.Evaluate(
            text,
            1000,
            TranslationSourceLanguage.Auto,
            TranslationTargetLanguage.SimplifiedChinese,
            "zh-CN");

        Assert.IsTrue(decision.ShouldTrigger, $"Expected '{text}' to trigger, got {decision.Reason}.");
        Assert.AreEqual(SelectionTranslationTriggerFilter.SkipReason.None, decision.Reason);
    }

    [TestMethod]
    [DataRow("Hello world", TranslationTargetLanguage.SimplifiedChinese, SelectionTranslationTriggerFilter.SkipReason.MatchesApplicationLanguage)]
    [DataRow("books", TranslationTargetLanguage.SimplifiedChinese, SelectionTranslationTriggerFilter.SkipReason.MatchesApplicationLanguage)]
    [DataRow("Hello world", TranslationTargetLanguage.English, SelectionTranslationTriggerFilter.SkipReason.MatchesTargetLanguage)]
    public void Evaluate_EnglishUiAutoMode_SkipsEnglishTextEvenWhenTargetIsChinese(
        string text,
        TranslationTargetLanguage target,
        SelectionTranslationTriggerFilter.SkipReason expectedReason)
    {
        var decision = SelectionTranslationTriggerFilter.Evaluate(
            text,
            1000,
            TranslationSourceLanguage.Auto,
            target,
            "en-US");

        Assert.IsFalse(decision.ShouldTrigger);
        Assert.AreEqual(expectedReason, decision.Reason);
    }

    [TestMethod]
    [DataRow("你好世界")]
    [DataRow("猫")]
    [DataRow("こんにちは")]
    [DataRow("Bonjour, comment ça va?")]
    [DataRow("Überraschung")]
    public void Evaluate_EnglishUiAutoMode_AllowsNonEnglishText(string text)
    {
        var decision = SelectionTranslationTriggerFilter.Evaluate(
            text,
            1000,
            TranslationSourceLanguage.Auto,
            TranslationTargetLanguage.English,
            "en-US");

        Assert.IsTrue(decision.ShouldTrigger, $"Expected '{text}' to trigger, got {decision.Reason}.");
    }

    [TestMethod]
    public void Evaluate_ExplicitSourceLanguage_RespectsUserSelectionAndAllowsChineseConversion()
    {
        var zhToEn = SelectionTranslationTriggerFilter.Evaluate(
            "你好世界",
            1000,
            TranslationSourceLanguage.SimplifiedChinese,
            TranslationTargetLanguage.English,
            "zh-CN");
        Assert.IsTrue(zhToEn.ShouldTrigger);

        var zhToEnMismatchEnglish = SelectionTranslationTriggerFilter.Evaluate(
            "Hello world",
            1000,
            TranslationSourceLanguage.SimplifiedChinese,
            TranslationTargetLanguage.English,
            "zh-CN");
        Assert.IsFalse(zhToEnMismatchEnglish.ShouldTrigger);
        Assert.AreEqual(SelectionTranslationTriggerFilter.SkipReason.SourceLanguageMismatch, zhToEnMismatchEnglish.Reason);

        var zhToEnMismatchJapanese = SelectionTranslationTriggerFilter.Evaluate(
            "こんにちは",
            1000,
            TranslationSourceLanguage.SimplifiedChinese,
            TranslationTargetLanguage.English,
            "zh-CN");
        Assert.IsFalse(zhToEnMismatchJapanese.ShouldTrigger);
        Assert.AreEqual(SelectionTranslationTriggerFilter.SkipReason.SourceLanguageMismatch, zhToEnMismatchJapanese.Reason);

        var zhSimpToTrad = SelectionTranslationTriggerFilter.Evaluate(
            "简体中文",
            1000,
            TranslationSourceLanguage.SimplifiedChinese,
            TranslationTargetLanguage.TraditionalChinese,
            "zh-CN");
        Assert.IsTrue(zhSimpToTrad.ShouldTrigger);

        var sameLang = SelectionTranslationTriggerFilter.Evaluate(
            "Hello world",
            1000,
            TranslationSourceLanguage.English,
            TranslationTargetLanguage.English,
            "zh-CN");
        Assert.IsFalse(sameLang.ShouldTrigger);
        Assert.AreEqual(SelectionTranslationTriggerFilter.SkipReason.SameSourceAndTarget, sameLang.Reason);
    }

    [TestMethod]
    [DataRow("", SelectionTranslationTriggerFilter.SkipReason.Empty)]
    [DataRow("   \t\r\n ", SelectionTranslationTriggerFilter.SkipReason.Empty)]
    [DataRow("123456", SelectionTranslationTriggerFilter.SkipReason.NoTranslatableText)]
    [DataRow("2026-11-21", SelectionTranslationTriggerFilter.SkipReason.NoTranslatableText)]
    [DataRow("$123.45", SelectionTranslationTriggerFilter.SkipReason.NoTranslatableText)]
    [DataRow("192.168.1.1", SelectionTranslationTriggerFilter.SkipReason.NoTranslatableText)]
    [DataRow("🎉🚀✅", SelectionTranslationTriggerFilter.SkipReason.NoTranslatableText)]
    [DataRow("...---!!!", SelectionTranslationTriggerFilter.SkipReason.NoTranslatableText)]
    [DataRow("a", SelectionTranslationTriggerFilter.SkipReason.NoTranslatableText)]
    [DataRow("C#", SelectionTranslationTriggerFilter.SkipReason.NoTranslatableText)]
    [DataRow("https://github.com/liaom/Kitopia", SelectionTranslationTriggerFilter.SkipReason.NonNaturalLanguage)]
    [DataRow("www.example.com", SelectionTranslationTriggerFilter.SkipReason.NonNaturalLanguage)]
    [DataRow("github.com/a/b", SelectionTranslationTriggerFilter.SkipReason.NonNaturalLanguage)]
    [DataRow("localhost:5111/api", SelectionTranslationTriggerFilter.SkipReason.NonNaturalLanguage)]
    [DataRow("user@example.com", SelectionTranslationTriggerFilter.SkipReason.NonNaturalLanguage)]
    [DataRow("2001:db8::1", SelectionTranslationTriggerFilter.SkipReason.NonNaturalLanguage)]
    [DataRow(@"C:\Users\liaom\test.txt", SelectionTranslationTriggerFilter.SkipReason.NonNaturalLanguage)]
    [DataRow(@"\\server\share\file.txt", SelectionTranslationTriggerFilter.SkipReason.NonNaturalLanguage)]
    [DataRow("/usr/local/bin/dotnet", SelectionTranslationTriggerFilter.SkipReason.NonNaturalLanguage)]
    [DataRow("./src/Program.cs", SelectionTranslationTriggerFilter.SkipReason.NonNaturalLanguage)]
    [DataRow("~/workspace/Kitopia", SelectionTranslationTriggerFilter.SkipReason.NonNaturalLanguage)]
    [DataRow("src/Kitopia.Desktop/Program.cs", SelectionTranslationTriggerFilter.SkipReason.NonNaturalLanguage)]
    [DataRow("550e8400-e29b-41d4-a716-446655440000", SelectionTranslationTriggerFilter.SkipReason.NonNaturalLanguage)]
    [DataRow("#0064FA", SelectionTranslationTriggerFilter.SkipReason.NonNaturalLanguage)]
    [DataRow("0xDEADBEEF", SelectionTranslationTriggerFilter.SkipReason.NonNaturalLanguage)]
    [DataRow("89a7842", SelectionTranslationTriggerFilter.SkipReason.NonNaturalLanguage)]
    [DataRow("v1.2.3", SelectionTranslationTriggerFilter.SkipReason.NonNaturalLanguage)]
    [DataRow("1.2.3-beta.1", SelectionTranslationTriggerFilter.SkipReason.NonNaturalLanguage)]
    [DataRow("net10.0", SelectionTranslationTriggerFilter.SkipReason.NonNaturalLanguage)]
    [DataRow("--verify-no-changes", SelectionTranslationTriggerFilter.SkipReason.NonNaturalLanguage)]
    [DataRow("$HOME", SelectionTranslationTriggerFilter.SkipReason.NonNaturalLanguage)]
    [DataRow("snake_case_identifier", SelectionTranslationTriggerFilter.SkipReason.NonNaturalLanguage)]
    [DataRow("System.Text.Json", SelectionTranslationTriggerFilter.SkipReason.NonNaturalLanguage)]
    [DataRow("foo.bar()", SelectionTranslationTriggerFilter.SkipReason.NonNaturalLanguage)]
    [DataRow("a::b", SelectionTranslationTriggerFilter.SkipReason.NonNaturalLanguage)]
    [DataRow("ptr->value", SelectionTranslationTriggerFilter.SkipReason.NonNaturalLanguage)]
    [DataRow("if (x != null) { return x; }", SelectionTranslationTriggerFilter.SkipReason.NonNaturalLanguage)]
    [DataRow("a + b == c && d != e", SelectionTranslationTriggerFilter.SkipReason.NonNaturalLanguage)]
    [DataRow("1 + 2 + 3 + 4 + 5 + a", SelectionTranslationTriggerFilter.SkipReason.NonNaturalLanguage)]
    public void Evaluate_NonTranslatableOrNonNaturalText_SkipsWithExpectedReason(
        string text,
        SelectionTranslationTriggerFilter.SkipReason expectedReason)
    {
        var decision = SelectionTranslationTriggerFilter.Evaluate(
            text,
            1000,
            TranslationSourceLanguage.Auto,
            TranslationTargetLanguage.SimplifiedChinese,
            "zh-CN");

        Assert.IsFalse(decision.ShouldTrigger, $"Expected '{text}' to be skipped.");
        Assert.AreEqual(expectedReason, decision.Reason);
    }

    [TestMethod]
    public void Evaluate_ExceedingMaximumLength_ReturnsTooLong()
    {
        var decision = SelectionTranslationTriggerFilter.Evaluate(
            new string('a', 1001),
            1000,
            TranslationSourceLanguage.Auto,
            TranslationTargetLanguage.SimplifiedChinese,
            "zh-CN");

        Assert.IsFalse(decision.ShouldTrigger);
        Assert.AreEqual(SelectionTranslationTriggerFilter.SkipReason.TooLong, decision.Reason);
    }
}
