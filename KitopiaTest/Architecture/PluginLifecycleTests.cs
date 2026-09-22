using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Services.Plugin;
using Kitopia.Desktop.Features.Utils;
using Kitopia.Desktop.Platform.Windows;
using Kitopia.Desktop.Pages;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Config;
using PluginCore.CustomScenario.Attribute.ConfigField;

namespace KitopiaTest.Architecture;

[TestClass]
[DoNotParallelize]
public sealed class PluginLifecycleTests
{
    [TestMethod]
    public async Task SettingPage_Unloaded_ReleasesConfigWithoutSavingChanges()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(MouseHotKeyTests));
        await session.Dispatch<bool>(async () =>
        {
            var previousServices = ServiceManager.Services;
            using var provider = new ServiceCollection().BuildServiceProvider();
            ServiceManager.Services = provider;
            var page = new SettingPage();
            var window = new Window { Content = page };
            try
            {
                var config = ShowAndUnloadSettings(page, window);
                for (var attempt = 0; attempt < 10 && config.IsAlive; attempt++)
                {
                    await Task.Delay(50);
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    GC.Collect();
                }
                Assert.IsFalse(config.IsAlive, "A cached settings page must not retain a plugin config through its controls.");
                GC.KeepAlive(page);
            }
            finally
            {
                window.Close();
                ServiceManager.Services = previousServices;
            }
            return true;
        }, CancellationToken.None);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ShowAndUnloadSettings(SettingPage page, Window window)
    {
        var config = new SettingsConfig { Name = "test-unload-settings" };
        page.ChangeConfig(config);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.IsTrue(page.IsLoaded);
        config.ConfigChanged += (_, _) => Assert.Fail("Releasing controls must not change configuration values.");
        window.Content = null;
        Dispatcher.UIThread.RunJobs();
        Assert.IsFalse(page.IsLoaded);
        Assert.IsEmpty(page.FindControl<StackPanel>("StackPanel")!.Children);
        Assert.AreEqual(HotKeyType.Mouse, config.Mode);
        return new WeakReference(config);
    }

    private sealed class SettingsConfig : ConfigBase
    {
        [ConfigField("Folders", "", fieldType: ConfigFieldType.目录列表)]
        public ObservableCollection<string> Folders = [];

        [ConfigField<HotKeyType>("Mode", "")]
        public HotKeyType Mode = HotKeyType.Mouse;

        [ConfigField("Clear folders", "", fieldType: ConfigFieldType.按钮, actionName: "Clear")]
        public void ClearFolders() => Folders.Clear();
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("config")]
    [DataRow("factory")]
    [DataRow("enable")]
    [DataRow("disable")]
    public async Task Unload_AfterLifecycleStage_CleansRegistrationsAndReportsRetainedContext(string failureStage)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(MouseHotKeyTests));
        await session.Dispatch<bool>(async () =>
        {
            await ExerciseLifecycleAsync(failureStage);
            return true;
        }, CancellationToken.None);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Unload_RequiresContextCollection_BlocksRetainedInstance(bool retainContext)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(MouseHotKeyTests));
        await session.Dispatch<bool>(async () =>
        {
            var name = "test-unload-" + Guid.NewGuid().ToString("N");
            var previousServices = ServiceManager.Services;
            using var provider = new ServiceCollection().AddSingleton<IHotKetImpl>(new HotKeyImpl()).BuildServiceProvider();
            ServiceManager.Services = provider;
            var info = new PluginLocalInfo
            {
                PluginBaseInfo = new PluginBaseInfo
                {
                    Name = name, NameSign = name, Version = "1.0.0", Main = "PluginUnload.dll", Dependencies = new()
                },
                Path = Path.Combine(AppContext.BaseDirectory, "PluginFixture") + Path.DirectorySeparatorChar,
                FullPath = Path.Combine(AppContext.BaseDirectory, "PluginFixture", "PluginUnload.dll")
            };
            try
            {
                var weak = await EnableAndRetainDuringUnloadAsync(info, retainContext);
                Assert.IsTrue(await PluginManager.UnloadCoreAsync(info));
                Assert.IsFalse(weak.IsAlive, "Success must mean the collectible context was actually reclaimed.");
                Assert.IsFalse(info.UnloadFailed);
                await PluginManager.EnableOneAsync(info);
                Assert.IsTrue(await PluginManager.UnloadCoreAsync(info));
            }
            finally
            {
                await PluginManager.UnloadCoreAsync(info);
                ServiceManager.Services = previousServices;
            }
            return true;
        }, CancellationToken.None);
    }

    // Keep strong references out of the caller's stack while it verifies GC completion.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> EnableAndRetainDuringUnloadAsync(PluginLocalInfo info, bool retainContext)
    {
        await PluginManager.EnableOneAsync(info);
        var context = PluginManager.GetEnablePlugins()[info.ToPlgString()].AssemblyLoadContext;
        var weak = new WeakReference(context, trackResurrection: true);
        if (retainContext)
        {
            Assert.IsFalse(await PluginManager.UnloadCoreAsync(info));
            Assert.IsTrue(info.UnloadFailed);
            Assert.IsTrue(weak.IsAlive);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => PluginManager.EnableOneAsync(info));
            GC.KeepAlive(context);
        }
        return weak;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task ExerciseLifecycleAsync(string failureStage)
    {
        var name = "test-lifecycle-" + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(Path.GetTempPath(), name);
        Directory.CreateDirectory(directory);
        var events = Path.Combine(directory, "events.txt");
        var key = name + "#PluginLifecycle.FixtureConfig";
        var configPath = KitopiaPaths.GetConfigFilePath(key);
        var json = JsonSerializer.Serialize(new { FailureStage = failureStage, EventsFile = events });
        File.WriteAllText(configPath, json);
        var hotkeys = new HotKeyImpl();
        using var provider = new ServiceCollection().AddSingleton<IHotKetImpl>(hotkeys)
            .AddSingleton<IConfigProvider, ConfigManger>().BuildServiceProvider();
        var previousServices = ServiceManager.Services;
        var previousPluginServices = PluginCore.Kitopia.ServiceProvider;
        ServiceManager.Services = provider;
        PluginCore.Kitopia.ServiceProvider = provider;
        var info = new PluginLocalInfo
        {
            PluginBaseInfo = new PluginBaseInfo
            {
                Name = name, NameSign = name, Version = "1.0.0", Main = "PluginLifecycle.dll", Dependencies = new()
            },
            Path = Path.Combine(AppContext.BaseDirectory, "PluginFixture") + Path.DirectorySeparatorChar,
            FullPath = Path.Combine(AppContext.BaseDirectory, "PluginFixture", "PluginLifecycle.dll")
        };
        WeakReference? context = null;
        try
        {
            if (failureStage is "config" or "factory" or "enable")
            {
                await Assert.ThrowsAsync<Exception>(() => PluginManager.EnableOneAsync(info));
                Assert.IsFalse(PluginManager.GetEnablePlugins().ContainsKey(name));
            }
            else
            {
                context = await EnableAndRetainDuringUnloadAsync(info, false);
                Assert.IsTrue(ConfigManger.AllConfigs.ContainsKey(key));
            }
            var unloaded = await PluginManager.UnloadCoreAsync(info);
            if (failureStage == "disable") Assert.IsFalse(unloaded);
            if (context is not null && unloaded) Assert.IsFalse(context.IsAlive);
            if (!unloaded)
            {
                Assert.IsTrue(info.UnloadFailed);
                await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => PluginManager.EnableOneAsync(info));
            }
            Assert.IsFalse(System.Runtime.Loader.AssemblyLoadContext.All.Any(context => context.Name == "PluginLifecycle.dll_plugin"));
            Assert.IsFalse(ConfigManger.AllConfigs.ContainsKey(key));
            Assert.IsEmpty(hotkeys.GetAllRegistered());
            Assert.IsFalse(PluginOverall.Features.ContainsKey(name));
            Assert.IsFalse(PluginOverall.SearchWindowInputDataAnalyzers.ContainsKey(name));
            Assert.AreEqual(json, File.ReadAllText(configPath));
            if (failureStage is "" or "enable" or "disable")
                CollectionAssert.AreEqual(new[] { "enabled", "stopped", "disposed" }, File.ReadAllLines(events));
        }
        finally
        {
            await PluginManager.UnloadCoreAsync(info);
            ServiceManager.Services = previousServices;
            PluginCore.Kitopia.ServiceProvider = previousPluginServices;
            ConfigManger.RemoveConfig(name);
            File.Delete(configPath);
            File.Delete(configPath + ".bak");
            Directory.Delete(directory, true);
        }
    }
}
