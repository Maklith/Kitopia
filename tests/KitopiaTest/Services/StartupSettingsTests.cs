using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Utils;
using Kitopia.Desktop.Pages;
using Kitopia.Feature.Localization;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Config;
using SettingsExpander = Kitopia.Desktop.Controls.SettingsExpander.SettingsExpander;

namespace KitopiaTest.Services;

[TestClass]
[DoNotParallelize]
public sealed class StartupSettingsTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Kitopia.Desktop.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia();

    [TestMethod]
    public void Config_OldAndSavedSettings_PreservesDefaultsAndNewPreferences()
    {
        var defaults = JsonSerializer.Deserialize<KitopiaConfig>("{}", ConfigManger.DefaultOptions)!;
        Assert.IsFalse(defaults.preferEarlyStartup);
        Assert.IsFalse(defaults.hideSearchWindowBorder);
        Assert.IsFalse(defaults.verticalEmptySearchResults);
        defaults.autoStart = false;
        defaults.preferEarlyStartup = true;
        defaults.hideSearchWindowBorder = true;
        defaults.verticalEmptySearchResults = true;
        var restored = JsonSerializer.Deserialize<KitopiaConfig>(
            JsonSerializer.Serialize(defaults, ConfigManger.DefaultOptions), ConfigManger.DefaultOptions)!;
        Assert.IsFalse(restored.autoStart);
        Assert.IsTrue(restored.preferEarlyStartup);
        Assert.IsTrue(restored.hideSearchWindowBorder);
        Assert.IsTrue(restored.verticalEmptySearchResults);
    }

    [TestMethod]
    [DataRow("success", false)]
    [DataRow("cancelled", false)]
    [DataRow("exception", false)]
    [DataRow("success", true)]
    [DataRow("cancelled", true)]
    [DataRow("exception", true)]
    public async Task StartupToggle_PendingThenCompleted_OnlyPersistsSuccessfulChanges(string outcome, bool reloadWhilePending)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(StartupSettingsTests));
        await session.Dispatch(async () =>
        {
            var originalServices = ServiceManager.Services;
            var originalConfigs = ConfigManger.Configs;
            var key = "test-startup-settings-" + Guid.NewGuid().ToString("N");
            var config = new KitopiaConfig { Name = key, autoStart = false, preferEarlyStartup = true };
            ConfigManger.Configs = new Dictionary<string, ConfigBase> { ["KitopiaConfig"] = config, [key] = config };
            var app = DispatchProxy.Create<IApplicationService, StartupSettingsProxy>();
            var appProxy = (StartupSettingsProxy)app;
            var toast = DispatchProxy.Create<IToastService, StartupSettingsProxy>();
            var toastProxy = (StartupSettingsProxy)toast;
            using var services = new ServiceCollection().AddSingleton(app).AddSingleton(toast).BuildServiceProvider();
            ServiceManager.Services = services;
            var configPath = KitopiaPaths.GetConfigFilePath(key);
            var originalJson = JsonSerializer.Serialize(config, ConfigManger.DefaultOptions);
            File.WriteAllText(configPath, originalJson);
            var page = new SettingPage();
            var window = new Window { Content = page, Width = 900, Height = 700 };
            try
            {
                page.ChangeConfig(config);
                window.Show();
                var rows = page.GetLogicalDescendants().OfType<SettingsExpander>().ToArray();
                var startupRow = rows.Single(row => Equals(row.Header, Lang.Get("lang.kitopia.start_automatically")));
                var earlyRow = rows.Single(row => Equals(row.Header, Lang.Get("lang.kitopia.prefer_early_startup")));
                var toggle = (ToggleSwitch)startupRow.Footer!;
                Assert.IsFalse(earlyRow.IsVisible);
                Assert.AreEqual(true, ((ToggleSwitch)earlyRow.Footer!).IsChecked);
                Assert.AreEqual(0, appProxy.Calls);
                toggle.IsChecked = true;
                Assert.AreEqual((true, true, true), appProxy.Request);
                Assert.IsFalse(config.autoStart);
                Assert.IsFalse(startupRow.IsEnabled);
                Assert.IsFalse(earlyRow.IsEnabled);
                Assert.AreEqual(originalJson, File.ReadAllText(configPath));
                if (reloadWhilePending)
                {
                    page.ChangeConfig(config);
                    rows = page.GetLogicalDescendants().OfType<SettingsExpander>().ToArray();
                    startupRow = rows.Single(row => Equals(row.Header, Lang.Get("lang.kitopia.start_automatically")));
                    earlyRow = rows.Single(row => Equals(row.Header, Lang.Get("lang.kitopia.prefer_early_startup")));
                    toggle = (ToggleSwitch)startupRow.Footer!;
                    Assert.IsFalse(startupRow.IsEnabled);
                    Assert.IsFalse(earlyRow.IsEnabled);
                }
                if (outcome == "exception") appProxy.Pending.SetException(new InvalidOperationException("test failure"));
                else appProxy.Pending.SetResult(outcome == "success");
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (!startupRow.IsEnabled && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(10);
                    Dispatcher.UIThread.RunJobs();
                }
                var success = outcome == "success";
                Assert.IsTrue(startupRow.IsEnabled);
                Assert.IsTrue(earlyRow.IsEnabled);
                Assert.AreEqual(success, config.autoStart);
                Assert.AreEqual(success, toggle.IsChecked);
                Assert.AreEqual(success, earlyRow.IsVisible);
                Assert.IsTrue(config.preferEarlyStartup);
                Assert.AreEqual(1, appProxy.Calls, "Reverting a toggle must not register startup again.");
                Assert.AreEqual(success ? 0 : 1, toastProxy.Calls);
                page.ChangeConfig(config); // Flush the existing save queue before checking persisted state.
                var saved = JsonSerializer.Deserialize<KitopiaConfig>(File.ReadAllText(configPath), ConfigManger.DefaultOptions)!;
                Assert.AreEqual(success, saved.autoStart);
                Assert.IsTrue(saved.preferEarlyStartup);
                if (!success) Assert.AreEqual(originalJson, File.ReadAllText(configPath));
            }
            finally
            {
                window.Close();
                ConfigManger.RemoveConfig(key);
                ConfigManger.Configs = originalConfigs;
                ServiceManager.Services = originalServices;
                File.Delete(configPath);
                File.Delete(configPath + ".bak");
            }
            return true;
        }, CancellationToken.None);
    }

    private class StartupSettingsProxy : DispatchProxy
    {
        public TaskCompletionSource<bool> Pending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public (bool, bool, bool) Request { get; private set; }
        public int Calls { get; private set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls++;
            if (targetMethod!.Name == nameof(IApplicationService.ChangeAutoStartAsync))
            {
                Request = ((bool)args![0]!, (bool)args[1]!, (bool)args[2]!);
                return Pending.Task;
            }
            if (targetMethod.Name == nameof(IToastService.Show)) return Pending.Task;
            throw new NotSupportedException(targetMethod.Name);
        }
    }
}
