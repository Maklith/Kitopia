using System.Globalization;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Platform.Windows;
using PluginCore;
using PluginCore.Config;

namespace KitopiaTest.Services;

[TestClass]
public sealed class ApplicationUpdateTranslationTests
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
    [DataRow("fr-FR", TranslationTargetLanguage.SimplifiedChinese)]
    public void GetTranslationTargetLanguage_MapsExpectedCultureToTargetLanguage(string cultureName, TranslationTargetLanguage expected)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var actual = ApplicationService.GetTranslationTargetLanguage(culture);
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    [DataRow("zh-CN", TranslationTargetLanguage.SimplifiedChinese)]
    [DataRow("zh-TW", TranslationTargetLanguage.TraditionalChinese)]
    [DataRow("en-US", TranslationTargetLanguage.English)]
    [DataRow("ja-JP", TranslationTargetLanguage.Japanese)]
    public void GetTranslationTargetLanguage_WithConfiguredLanguageString_ReturnsExpectedTargetLanguage(
        string configuredLanguage, TranslationTargetLanguage expected)
    {
        var actual = ApplicationService.GetTranslationTargetLanguage(configuredLanguage);
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void GetTranslationTargetLanguage_ReadsConfigMangerConfigLanguage()
    {
        var originalConfigs = ConfigManger.Configs;
        try
        {
            ConfigManger.Configs = new Dictionary<string, ConfigBase>
            {
                ["KitopiaConfig"] = new KitopiaConfig { language = "en-US" }
            };
            Assert.AreEqual(TranslationTargetLanguage.English, ApplicationService.GetTranslationTargetLanguage());

            ConfigManger.Configs = new Dictionary<string, ConfigBase>
            {
                ["KitopiaConfig"] = new KitopiaConfig { language = "zh-TW" }
            };
            Assert.AreEqual(TranslationTargetLanguage.TraditionalChinese, ApplicationService.GetTranslationTargetLanguage());

            ConfigManger.Configs = new Dictionary<string, ConfigBase>
            {
                ["KitopiaConfig"] = new KitopiaConfig { language = "" }
            };
            Assert.AreEqual(TranslationTargetLanguage.Japanese,
                ApplicationService.GetTranslationTargetLanguage(fallbackCulture: CultureInfo.GetCultureInfo("ja-JP")));
        }
        finally
        {
            ConfigManger.Configs = originalConfigs;
        }
    }

    [TestMethod]
    public async Task TranslateReleaseNotesAsync_WithValidReleaseNotes_CallsTranslationServiceAndReturnsResult()
    {
        var mockService = new MockTranslationService("版本 1.2.0：错误修复");
        var notes = "Version 1.2.0: bug fixes";
        var result = await ApplicationService.TranslateReleaseNotesAsync(
            notes,
            mockService,
            CultureInfo.GetCultureInfo("zh-CN"));

        Assert.AreEqual("版本 1.2.0：错误修复", result);
        Assert.AreEqual(notes, mockService.LastText);
        Assert.AreEqual(TranslationSourceLanguage.Auto, mockService.LastSource);
        Assert.AreEqual(TranslationTargetLanguage.SimplifiedChinese, mockService.LastTarget);
    }

    [TestMethod]
    public async Task TranslateReleaseNotesAsync_WhenTranslationThrows_ReturnsOriginalNotes()
    {
        var mockService = new MockTranslationService(throwException: true);
        const string notes = "Original release notes";
        var result = await ApplicationService.TranslateReleaseNotesAsync(
            notes,
            mockService,
            CultureInfo.GetCultureInfo("en-US"));

        Assert.AreEqual(notes, result);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public async Task TranslateReleaseNotesAsync_WhenReleaseNotesEmptyOrNull_ReturnsOriginalWithoutCallingService(string? notes)
    {
        var mockService = new MockTranslationService("translated");
        var result = await ApplicationService.TranslateReleaseNotesAsync(
            notes,
            mockService,
            CultureInfo.GetCultureInfo("zh-CN"));

        Assert.AreEqual(notes, result);
        Assert.AreEqual(0, mockService.CallCount);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public async Task TranslateReleaseNotesAsync_WhenTranslationReturnsWhitespace_ReturnsOriginalNotes(string translated)
    {
        var mockService = new MockTranslationService(translated);
        const string notes = "Original notes";
        var result = await ApplicationService.TranslateReleaseNotesAsync(
            notes,
            mockService,
            CultureInfo.GetCultureInfo("ja-JP"));

        Assert.AreEqual(notes, result);
    }

    [TestMethod]
    public async Task TranslateReleaseNotesAsync_WhenServiceNull_ReturnsOriginalNotes()
    {
        const string notes = "Original notes";
        var result = await ApplicationService.TranslateReleaseNotesAsync(
            notes,
            translationService: null,
            CultureInfo.GetCultureInfo("en-US"));

        Assert.AreEqual(notes, result);
    }

    private sealed class MockTranslationService(string result = "", bool throwException = false) : ITranslationService
    {
        public string? LastText { get; private set; }
        public TranslationSourceLanguage? LastSource { get; private set; }
        public TranslationTargetLanguage? LastTarget { get; private set; }
        public int CallCount { get; private set; }

        public Task<string> TranslateAsync(
            string text,
            TranslationSourceLanguage sourceLanguage = TranslationSourceLanguage.Auto,
            TranslationTargetLanguage targetLanguage = TranslationTargetLanguage.SimplifiedChinese,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastText = text;
            LastSource = sourceLanguage;
            LastTarget = targetLanguage;

            if (throwException)
            {
                throw new HttpRequestException("Mock network failure");
            }

            return Task.FromResult(result);
        }
    }
}
