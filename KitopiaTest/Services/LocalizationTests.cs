using System.Diagnostics;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Account;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.ViewModel.Account;
using Kitopia.Desktop.Features.Utils;
using Kitopia.Desktop.Pages;
using Kitopia.Desktop.Controls;
using Kitopia.Desktop.Features.ViewModel.Pages;
using Kitopia.Desktop.Features.ViewModel.Pages.plugin;
using Kitopia.Desktop.Platform.Windows;
using Kitopia.Feature.Avalonia.Localization;
using Kitopia.Feature.Localization;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Config;
using SettingsExpander = Kitopia.Desktop.Controls.SettingsExpander.SettingsExpander;

namespace KitopiaTest.Services;

[TestClass]
[DoNotParallelize]
public sealed class LocalizationTests
{
    public TestContext TestContext { get; set; } = null!;

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Kitopia.Desktop.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia();

    [TestMethod]
    [DataRow("en-GB", "en-US", "Cancel")]
    [DataRow("zh-TW", "zh-CN", "取消")]
    [DataRow("fr-FR", "zh-CN", "取消")]
    [DataRow("invalid-language!", "zh-CN", "取消")]
    public void UseLanguage_RegionalOrUnsupportedLanguage_ResolvesDictionary(string requested, string expected, string cancel)
    {
        var lang = new Lang();
        lang.UseLanguage(requested);
        Assert.AreEqual(expected, lang.Language);
        Assert.AreEqual(cancel, lang["lang.kitopia.cancel"]);
        Assert.AreEqual("Plugin.CustomText", lang["Plugin.CustomText"]);
    }

    [TestMethod]
    public void Dictionaries_KeysAndFormatArguments_AreConsistent()
    {
        var assembly = typeof(Lang).Assembly;
        using var fallbackStream = assembly.GetManifestResourceStream("Kitopia.Feature.lang.zh-CN.json")!;
        using var fallback = JsonDocument.Parse(fallbackStream);
        var expected = fallback.RootElement.EnumerateObject().ToDictionary(entry => entry.Name, entry => entry.Value.GetString()!);
        foreach (var code in Lang.Current.Languages)
        {
            using var stream = assembly.GetManifestResourceStream($"Kitopia.Feature.lang.{code}.json")!;
            using var dictionary = JsonDocument.Parse(stream);
            var entries = dictionary.RootElement.EnumerateObject().ToArray();
            Assert.AreEqual(entries.Length, entries.Select(entry => entry.Name).Distinct().Count(), $"Duplicate keys: {code}");
            CollectionAssert.AreEquivalent(expected.Keys.ToArray(), entries.Select(entry => entry.Name).ToArray(), code);
            foreach (var entry in entries)
            {
                Assert.IsTrue(Regex.IsMatch(entry.Name, @"^lang\.kitopia\.[a-z0-9_]+(?:\.[a-z0-9_]+)*$"), entry.Name);
                Assert.IsFalse(string.IsNullOrWhiteSpace(entry.Value.GetString()), $"Empty translation: {code}/{entry.Name}");
                Assert.AreEqual(CompositeFormat.Parse(expected[entry.Name]).MinimumArgumentCount,
                    CompositeFormat.Parse(entry.Value.GetString()!).MinimumArgumentCount, $"Format arguments: {code}/{entry.Name}");
            }
        }
    }

    [TestMethod]
    public async Task LanguagePicker_ChangeLanguage_RefreshesResourcesSettingsAndPersistsSelection()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(LocalizationTests));
        await session.Dispatch(() =>
        {
            var originalServices = ServiceManager.Services;
            var originalConfigs = ConfigManger.Configs;
            var originalLanguage = Lang.Current.Language;
            using var services = new ServiceCollection().AddSingleton<IAccountService, AccountService>()
                .AddSingleton<AccountCardViewModel>().BuildServiceProvider();
            ServiceManager.Services = services;
            var key = "test-language-" + Guid.NewGuid().ToString("N");
            var config = new KitopiaConfig { Name = key, language = "zh-CN" };
            ConfigManger.Configs = new Dictionary<string, ConfigBase> { [key] = config };
            var page = new SettingPage();
            var window = new Window { Content = page, Width = 1100, Height = 760 };
            var path = KitopiaPaths.GetConfigFilePath(key);
            var application = Application.Current!;
            var resourceChangeCount = 0;
            EventHandler<ResourcesChangedEventArgs> onResourcesChanged = (_, _) => resourceChangeCount++;
            application.ResourcesChanged += onResourcesChanged;
            try
            {
                Lang.Current.UseLanguage("zh-CN");
                page.ChangeConfig(config);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var rows = page.GetLogicalDescendants().OfType<SettingsExpander>().ToArray();
                var languageRow = rows.Single(row => Equals(row.Header, "界面语言"));
                var themeRow = rows.Single(row => Equals(row.Header, "主题选择"));
                var picker = (ComboBox)languageRow.Footer!;
                var liveLabel = new TextBlock();
                liveLabel.Bind(TextBlock.TextProperty, new LangExtension("lang.kitopia.value_plugins")
                {
                    Value = new Avalonia.Data.Binding { Source = new { Count = 3 }, Path = "Count" }
                }.ProvideValue(null!) as Avalonia.Data.BindingBase ?? throw new InvalidOperationException());
                Assert.AreEqual("共 3 个插件", liveLabel.Text);
                var fallbackLabel = new TextBlock();
                fallbackLabel.Bind(TextBlock.TextProperty, (Avalonia.Data.BindingBase)new LangExtension
                {
                    FallbackKey = "lang.kitopia.no_release_notes",
                    Value = new Avalonia.Data.Binding { Source = new { Detail = (string?)null }, Path = "Detail" }
                }.ProvideValue(null!));
                Assert.AreEqual("未填写说明", fallbackLabel.Text);

                resourceChangeCount = 0;
                var switchTimer = Stopwatch.StartNew();
                picker.SelectedItem = "en-US";
                Dispatcher.UIThread.RunJobs();
                switchTimer.Stop();
                TestContext.WriteLine($"zh-CN -> en-US: {resourceChangeCount} resource notifications, {switchTimer.Elapsed.TotalMilliseconds:F1} ms");
                Assert.AreEqual(1, resourceChangeCount, "A language switch must refresh application resources only once.");
                Assert.AreEqual("en-US", config.language);
                Assert.AreEqual("Display language", languageRow.Header);
                Assert.AreEqual("Theme", themeRow.Header);
                Assert.AreEqual("3 plugins", liveLabel.Text);
                Assert.AreEqual("No release notes", fallbackLabel.Text);
                Assert.AreEqual("Cancel", Application.Current!.FindResource("lang.kitopia.cancel"));
                page.ChangeConfig(config);
                var restored = JsonSerializer.Deserialize<KitopiaConfig>(File.ReadAllText(path), ConfigManger.DefaultOptions)!;
                Assert.AreEqual("en-US", restored.language);
                page.ChangeConfig(restored);
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual("en-US", ((ComboBox)page.GetLogicalDescendants().OfType<SettingsExpander>()
                    .Single(row => Equals(row.Header, "Display language")).Footer!).SelectedItem);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var screenshot = window.CaptureRenderedFrame();
                Assert.IsNotNull(screenshot);
                var screenshotPath = Path.Combine(TestContext.TestRunDirectory!, "settings-en-US.png");
                screenshot.Save(screenshotPath);
                TestContext.AddResultFile(screenshotPath);
                window.Width = 800;
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var compactScreenshot = window.CaptureRenderedFrame();
                Assert.IsNotNull(compactScreenshot);
                var compactPath = Path.Combine(TestContext.TestRunDirectory!, "settings-en-US-800.png");
                compactScreenshot.Save(compactPath);
                TestContext.AddResultFile(compactPath);

                resourceChangeCount = 0;
                switchTimer.Restart();
                Lang.Current.UseLanguage("zh-CN");
                Dispatcher.UIThread.RunJobs();
                switchTimer.Stop();
                TestContext.WriteLine($"en-US -> zh-CN: {resourceChangeCount} resource notifications, {switchTimer.Elapsed.TotalMilliseconds:F1} ms");
                Assert.AreEqual(1, resourceChangeCount);
                Assert.AreEqual("共 3 个插件", liveLabel.Text);
                Assert.AreEqual("未填写说明", fallbackLabel.Text);
                Assert.AreEqual("取消", Application.Current.FindResource("lang.kitopia.cancel"));
                foreach (var resourceKey in Lang.Current.Keys)
                    Assert.AreEqual(Lang.Get(resourceKey), application.FindResource("" + resourceKey), resourceKey);

                resourceChangeCount = 0;
                Lang.Current.UseLanguage("zh-CN");
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(0, resourceChangeCount, "Selecting the active language must not refresh resources.");
            }
            finally
            {
                application.ResourcesChanged -= onResourcesChanged;
                window.Close();
                ConfigManger.RemoveConfig(key);
                ConfigManger.Configs = originalConfigs;
                ServiceManager.Services = originalServices;
                Lang.Current.UseLanguage(originalLanguage);
                File.Delete(path);
                File.Delete(path + ".bak");
            }
        }, CancellationToken.None);
    }

    [TestMethod]
    public void Config_LegacyJson_DefaultsToSystemLanguage()
    {
        var config = JsonSerializer.Deserialize<KitopiaConfig>("{}", ConfigManger.DefaultOptions)!;
        Assert.AreEqual(string.Empty, config.language);
    }

    [TestMethod]
    public async Task PluginSettings_LanguageChanges_RefreshesSelectionAndGroupHeaders()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(LocalizationTests));
        var verified = await session.Dispatch(() =>
        {
            var previousServices = ServiceManager.Services;
            var previousConfigs = ConfigManger.Configs;
            var previousLanguage = Lang.Current.Language;
            var assembly = typeof(KitopiaEx.Config).Assembly;
            var needsRegistration = !Lang.Current.Keys.Contains("lang.kitopiaex.kitopiaex_settings");
            using var services = new ServiceCollection().AddSingleton<IAccountService, AccountService>()
                .AddSingleton<AccountCardViewModel>().BuildServiceProvider();
            ServiceManager.Services = services;
            const string key = "kitopiaex#KitopiaEx.Config";
            ConfigManger.Configs = new Dictionary<string, ConfigBase>
            {
                ["KitopiaConfig"] = new KitopiaConfig { Name = "KitopiaConfig" },
                [key] = new KitopiaEx.Config { Name = key }
            };
            var selectionWindow = new Window { Width = 900, Height = 600 };
            var settingsWindow = new Window { Width = 900, Height = 700 };
            try
            {
                if (needsRegistration) Lang.Current.RegisterAssembly(assembly);
                Lang.Current.UseLanguage("zh-CN");
                var viewModel = new PluginSettingViewModel();
                viewModel.LoadByPluginInfo("kitopiaex");
                var selection = new PluginSettingSelectPage { DataContext = viewModel };
                selectionWindow.Content = selection;
                selectionWindow.Show();
                var settings = new SettingPage();
                settings.LoadAllConfigs();
                settingsWindow.Content = settings;
                settingsWindow.Show();
                Dispatcher.UIThread.RunJobs();
                var title = selection.GetLogicalDescendants().OfType<TextBlock>()
                    .Single(label => label.Text == "KitopiaEx主配置文件");
                var header = settings.GetLogicalDescendants().OfType<Expander>()
                    .Select(expander => expander.Header).OfType<TextBlock>().Single();

                foreach (var language in new[] { "en-US", "zh-CN" })
                {
                    Lang.Current.UseLanguage(language);
                    Dispatcher.UIThread.RunJobs();
                    var expected = language == "en-US" ? "KitopiaEx settings" : "KitopiaEx主配置文件";
                    Assert.AreEqual(expected, title.Text);
                    Assert.AreEqual(expected, header.Text);
                    Assert.AreEqual("lang.kitopiaex.kitopiaex_settings", viewModel.SettingItems.Single().Title);
                }
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var screenshot = selectionWindow.CaptureRenderedFrame();
                Assert.IsNotNull(screenshot);
                var screenshotDirectory = Path.Combine(AppContext.BaseDirectory, "TestResults", "plugin-localization");
                Directory.CreateDirectory(screenshotDirectory);
                var screenshotPath = Path.Combine(screenshotDirectory, "plugin-settings-zh-CN.png");
                screenshot.Save(screenshotPath);
                TestContext.AddResultFile(screenshotPath);
            }
            finally
            {
                selectionWindow.Close();
                settingsWindow.Close();
                ConfigManger.Configs = previousConfigs;
                ServiceManager.Services = previousServices;
                if (needsRegistration) Lang.Current.UnregisterAssembly(assembly.GetName().Name!);
                Lang.Current.UseLanguage(previousLanguage);
            }
            return true;
        }, CancellationToken.None);
        Assert.IsTrue(verified);
    }

    [TestMethod]
    public async Task HotkeyPage_LanguageChanges_RefreshesTitlesKeysAndScope()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(LocalizationTests));
        await session.Dispatch(() =>
        {
            var previousServices = ServiceManager.Services;
            var previousLanguage = Lang.Current.Language;
            var hotkeys = new HotKeyImpl();
            var config = new KitopiaConfig();
            var models = new[] { config.searchHotKey, config.mouseHotkey, config.topMostWindowHotKey, config.screenShotHotKey };
            using var services = new ServiceCollection().AddSingleton<IHotKetImpl>(hotkeys).BuildServiceProvider();
            ServiceManager.Services = services;
            var window = new Window { Width = 1100, Height = 720 };
            HotKeyManagerPageViewModel? viewModel = null;
            try
            {
                Lang.Current.UseLanguage("zh-CN");
                foreach (var model in models)
                {
                    Assert.IsTrue(hotkeys.Register(model, _ => { }, false));
                    model.IsEnabled = true;
                }
                viewModel = new HotKeyManagerPageViewModel();
                window.Content = new HotKeyManagerPage { DataContext = viewModel };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var displays = window.GetLogicalDescendants().OfType<HotKeyShow>().ToArray();
                var preview = displays.Single(display => ReferenceEquals(display.HotKeyModel, config.mouseHotkey));
                Assert.AreEqual("空格", preview.KeyName);
                Lang.Current.UseLanguage("en-US");
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual("Space", preview.KeyName);
                Assert.AreEqual("Only in explorer.exe", preview.ScopeDescription);
                Assert.AreEqual("All processes", displays.Single(display => ReferenceEquals(display.HotKeyModel, config.searchHotKey)).ScopeDescription);
                Assert.IsTrue(window.GetLogicalDescendants().OfType<TextBlock>().Any(label => label.Text == "File preview"));
                Assert.IsTrue(window.GetLogicalDescendants().OfType<TextBlock>().Any(label => label.Text == "Press Space to preview selected files"));
                Assert.AreEqual("Space", Lang.Get(EKey.空格));
                Assert.AreEqual("简体中文", Lang.Get("简体中文"), "User text must not be treated as a translation key.");
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = window.CaptureRenderedFrame();
                Assert.IsNotNull(frame);
                var screenshot = Path.Combine(TestContext.TestRunDirectory!, "hotkeys-en-US.png");
                frame.Save(screenshot);
                TestContext.AddResultFile(screenshot);
                window.Width = 800;
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var compact = window.CaptureRenderedFrame();
                Assert.IsNotNull(compact);
                var compactPath = Path.Combine(TestContext.TestRunDirectory!, "hotkeys-en-US-800.png");
                compact.Save(compactPath);
                TestContext.AddResultFile(compactPath);
                Lang.Current.UseLanguage("zh-CN");
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual("空格", preview.KeyName);
                Assert.AreEqual("仅在 explorer.exe 生效", preview.ScopeDescription);
            }
            finally
            {
                window.Close();
                if (viewModel is not null) WeakReferenceMessenger.Default.UnregisterAll(viewModel);
                foreach (var model in models) hotkeys.Remove(model.UUID);
                ServiceManager.Services = previousServices;
                Lang.Current.UseLanguage(previousLanguage);
            }
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task PluginSdk_DictionariesAndBindings_FollowLanguageAndReleaseOnUnload()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(LocalizationTests));
        await session.Dispatch(() =>
        {
            var previousLanguage = Lang.Current.Language;
            var context = new AssemblyLoadContext("localization-fixture", isCollectible: true);
            var assembly = context.LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, "PluginFixture", "PluginLifecycle.dll"));
            try
            {
                Lang.Current.RegisterAssembly(assembly);
                PluginCore.Localization.Lang.Lookup = Lang.Get;
                PluginCore.Localization.Lang.BindingSource = Lang.Current;
                var label = new TextBlock();
                using var binding = label.Bind(TextBlock.TextProperty, (Avalonia.Data.BindingBase)new PluginCore.Localization.LangExtension("lang.pluginlifecycle.title").ProvideValue(null!));
                Lang.Current.UseLanguage("en-US");
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual("Lifecycle plugin", label.Text);
                Assert.AreEqual("仅默认语言", PluginCore.Localization.Lang.Get("lang.pluginlifecycle.only_default"));
                Assert.AreEqual("Lifecycle plugin", Application.Current!.FindResource("lang.pluginlifecycle.title"));
                Lang.Current.UseLanguage("zh-CN");
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual("生命周期插件", label.Text);
                Lang.Current.UnregisterAssembly("PluginLifecycle");
                Dispatcher.UIThread.RunJobs();
                Assert.IsFalse(Lang.Current.Keys.Contains("lang.pluginlifecycle.title"));
                Assert.AreEqual("lang.pluginlifecycle.title", label.Text);
                Assert.IsNull(Application.Current.FindResource("lang.pluginlifecycle.title"));
            }
            finally
            {
                Lang.Current.UnregisterAssembly("PluginLifecycle");
                context.Unload();
                Lang.Current.UseLanguage(previousLanguage);
            }
        }, CancellationToken.None);
    }

    [TestMethod]
    public void ClientAndPluginKeys_ReferencesAndDictionaries_AreComplete()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kitopia.sln"))) directory = directory.Parent;
        Assert.IsNotNull(directory);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in new[] { "Kitopia.Feature", "KitopiaEx" })
        {
            var dictionaries = Directory.EnumerateFiles(Path.Combine(directory.FullName, project, "lang"), "*.json")
                .ToDictionary(file => file, file => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file))!);
            var fallback = dictionaries.Single(entry => entry.Key.EndsWith("zh-CN.json", StringComparison.Ordinal)).Value;
            foreach (var (file, dictionary) in dictionaries)
            {
                CollectionAssert.AreEquivalent(fallback.Keys.ToArray(), dictionary.Keys.ToArray(), file);
                foreach (var (key, value) in dictionary)
                {
                    Assert.IsTrue(Regex.IsMatch(key, @"^lang\.[a-z0-9_]+(?:\.[a-z0-9_]+)+$"), key);
                    Assert.AreEqual(CompositeFormat.Parse(fallback[key]).MinimumArgumentCount, CompositeFormat.Parse(value).MinimumArgumentCount, key);
                }
            }
            keys.UnionWith(fallback.Keys);
        }
        foreach (var project in new[] { "Kitopia.Desktop", "Kitopia.Desktop.Features", "Kitopia.Desktop.PluginSdk", "Kitopia.Desktop.Platform.Windows", "Kitopia.Feature.Avalonia", "KitopiaEx", "Mobile/Kitopia.Mobile" })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(directory.FullName, project), "*", SearchOption.AllDirectories)
                     .Where(file => Path.GetExtension(file) is ".cs" or ".axaml")
                     .Where(file => !file.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj")))
        {
            var source = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(source, @"\blang\.[a-z0-9_]+(?:\.[a-z0-9_]+)+"))
                Assert.IsTrue(keys.Contains(match.Value), $"Missing key {match.Value}: {file}");
            Assert.IsFalse(Regex.IsMatch(source, @"\{DynamicResource ['\"" ]*Lang\.|Lang\.(?:Get|Format)\(\""[^\"" ]*\p{IsCJKUnifiedIdeographs}"), file);
        }
        foreach (var project in new[] { "KitopiaEx", "OnnxRuntime.CPU", "OnnxRuntime.Gpu.Win", "OnnxRuntime.OpenVino", "HasDependencyPlugin", "HasDependencyPluginBase" })
        {
            var references = XDocument.Load(Path.Combine(directory.FullName, project, project + ".csproj")).Descendants("ProjectReference");
            Assert.IsTrue(references.All(reference => reference.Attribute("Include")!.Value.Contains("Kitopia.Desktop.PluginSdk")
                || project == "HasDependencyPlugin" && reference.Attribute("Include")!.Value.Contains("HasDependencyPluginBase")), project);
        }
        Assert.IsFalse(XDocument.Load(Path.Combine(directory.FullName, "Kitopia.Desktop.PluginSdk/Kitopia.Desktop.PluginSdk.csproj")).Descendants("ProjectReference").Any());
    }

    [TestMethod]
    public void LocalizedViews_ResourceAndFormatKeys_ExistInFallbackDictionary()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kitopia.sln")))
            directory = directory.Parent;
        Assert.IsNotNull(directory);
        var lang = new Lang();
        var keys = lang.Keys.ToHashSet(StringComparer.Ordinal);
        foreach (var project in new[] { "Kitopia.Desktop", "Kitopia.Desktop.Features", "Kitopia.Desktop.Platform.Windows", "Kitopia.Feature.Avalonia", "Mobile/Kitopia.Mobile" })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(directory.FullName, project), "*.axaml", SearchOption.AllDirectories))
            {
                foreach (var attribute in XDocument.Load(file).Descendants().Attributes())
                {
                    var match = Regex.Match(attribute.Value, @"^\{DynamicResource '?((?:lang\.)[^'}]+)'?\}$|^\{lang:Lang '([^']+)', Value=");
                    if (!match.Success) continue;
                    var key = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                    if (key.StartsWith("{}", StringComparison.Ordinal)) key = key[2..];
                    Assert.IsTrue(keys.Contains(key), $"Missing dictionary key {key}: {file}");
                }
            }
        }
    }
}
