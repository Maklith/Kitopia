using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Kitopia.Desktop.Features.CustomScenario;
using Kitopia.Desktop.Features.PluginHost.Services;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Utils;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.CustomScenario;

namespace KitopiaTest.CustomScenario;

[TestClass]
[DoNotParallelize]
public sealed class ScenarioPersistenceTests
{
    [TestMethod]
    public void Save_SerializationFailure_PreservesFileAndUnsavedState()
    {
        var scenario = NewScenario();
        var path = KitopiaPaths.GetCustomScenarioFilePath(scenario.Uuid);
        var previousServices = ServiceManager.Services;
        using var provider = CreateServices();
        ServiceManager.Services = provider;
        File.WriteAllText(path, "previous");
        scenario.Values.Add("unsupported", new CustomScenarioValue(typeof(Guid), Guid.NewGuid())
        {
            IsSelf = true
        });
        try
        {
            Assert.IsFalse(CustomScenarioManger.Save(scenario));
            Assert.AreEqual("previous", File.ReadAllText(path));
            Assert.IsFalse(CustomScenarioManger.CustomScenarios.Contains(scenario));
        }
        finally
        {
            ServiceManager.Services = previousServices;
            File.Delete(path);
            CustomScenarioManger.CustomScenarios.Remove(scenario);
            scenario.Dispose();
        }
    }

    [TestMethod]
    public void Save_TemporaryValue_PersistsDefinitionWithoutClearingRuntimeValue()
    {
        var scenario = NewScenario();
        var path = KitopiaPaths.GetCustomScenarioFilePath(scenario.Uuid);
        var previousServices = ServiceManager.Services;
        using var provider = CreateServices();
        ServiceManager.Services = provider;
        CustomScenarioManger.CustomScenarios.Add(scenario);
        scenario.TempValue.Add("counter", new CustomScenarioValue(typeof(int), 23));
        try
        {
            Assert.IsTrue(CustomScenarioManger.Save(scenario));
            Assert.AreEqual(23, scenario.TempValue["counter"].Value);
            var loaded = JsonSerializer.Deserialize<Kitopia.Desktop.Features.CustomScenario.CustomScenario>(
                File.ReadAllText(path), ConfigManger.DefaultOptions);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(typeof(int), loaded.TempValue["counter"].SerializeType);
            Assert.IsNull(loaded.TempValue["counter"].Value);
            loaded.Dispose();
        }
        finally
        {
            ServiceManager.Services = previousServices;
            File.Delete(path);
            CustomScenarioManger.CustomScenarios.Remove(scenario);
            scenario.Dispose();
        }
    }

    [TestMethod]
    public void Save_HotKeyRegistrationFailure_StillReportsPersistedScenario()
    {
        var scenario = NewScenario();
        var path = KitopiaPaths.GetCustomScenarioFilePath(scenario.Uuid);
        var previousServices = ServiceManager.Services;
        using var provider = CreateServices();
        ServiceManager.Services = provider;
        scenario.RunHotKey = new HotKeyModel { IsEnabled = true };
        try
        {
            Assert.IsTrue(CustomScenarioManger.Save(scenario));
            Assert.IsTrue(File.Exists(path));
            Assert.IsTrue(CustomScenarioManger.CustomScenarios.Contains(scenario));
        }
        finally
        {
            ServiceManager.Services = previousServices;
            File.Delete(path);
            CustomScenarioManger.CustomScenarios.Remove(scenario);
            scenario.Dispose();
        }
    }

    [TestMethod]
    public void Load_MissingPluginType_KeepsManageableFailureEntry()
    {
        var scenario = NewScenario();
        var path = KitopiaPaths.GetCustomScenarioFilePath(scenario.Uuid);
        var previousServices = ServiceManager.Services;
        using var provider = CreateServices();
        ServiceManager.Services = provider;
        scenario.Values.Add("input", new CustomScenarioValue(typeof(int), 1) { IsSelf = true });
        var json = JsonSerializer.Serialize(scenario, ConfigManger.DefaultOptions)
            .Replace("System System.Int32", "AbsentPlugin Missing.Type", StringComparison.Ordinal);
        File.WriteAllText(path, json);
        try
        {
            CustomScenarioManger.Load(new FileInfo(path));

            var failed = CustomScenarioManger.CustomScenarios.Single(item => item.Uuid == scenario.Uuid);
            Assert.IsFalse(failed.HasInit);
            Assert.AreEqual(scenario.Name, failed.Name);
            StringAssert.Contains(failed.InitError!, "AbsentPlugin");
        }
        finally
        {
            ServiceManager.Services = previousServices;
            File.Delete(path);
            foreach (var item in CustomScenarioManger.CustomScenarios.Where(item => item.Uuid == scenario.Uuid).ToArray())
            {
                CustomScenarioManger.CustomScenarios.Remove(item);
                item.Dispose();
            }
            scenario.Dispose();
        }
    }

    [TestMethod]
    public void Load_MissingPluginTrigger_PreservesSelectionAndRecoversWhenRegistered()
    {
        using var scenario = NewScenario();
        scenario.Nodes.Add(new ScenarioMethod(ScenarioMethodType.OneToTwo).GenerateNode());
        scenario.Nodes.Add(new ScenarioMethod(ScenarioMethodType.OneToTwo).GenerateNode());
        var trigger = "fixture_" + Guid.NewGuid().ToString("N");
        scenario.AutoTriggers.Add(trigger);
        var path = KitopiaPaths.GetCustomScenarioFilePath(scenario.Uuid);
        var previousServices = ServiceManager.Services;
        using var provider = CreateServices();
        ServiceManager.Services = provider;
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(scenario, ConfigManger.DefaultOptions));
            CustomScenarioManger.Load(new FileInfo(path));
            var failed = CustomScenarioManger.CustomScenarios.Single(item => item.Uuid == scenario.Uuid);
            Assert.IsFalse(failed.HasInit);
            StringAssert.Contains(failed.InitError!, trigger);
            CollectionAssert.AreEqual(new[] { trigger }, failed.AutoTriggers.ToArray());
            CustomScenarioManger.CustomScenarios.Remove(failed);
            failed.Dispose();

            CustomScenarioGlobe.Triggers.Add(trigger, new CustomScenarioTriggerInfo { PluginInfo = "fixture" });
            CustomScenarioManger.Load(new FileInfo(path));
            var recovered = CustomScenarioManger.CustomScenarios.Single(item => item.Uuid == scenario.Uuid);
            Assert.IsTrue(recovered.HasInit);
            Assert.IsNull(recovered.InitError);
            Assert.IsTrue(recovered.IsUseThePlugin("fixture"));
        }
        finally
        {
            foreach (var item in CustomScenarioManger.CustomScenarios.Where(item => item.Uuid == scenario.Uuid).ToArray())
            {
                CustomScenarioManger.CustomScenarios.Remove(item);
                item.Dispose();
            }
            CustomScenarioGlobe.Triggers.Remove(trigger);
            File.Delete(path);
            ServiceManager.Services = previousServices;
        }
    }

    private static Kitopia.Desktop.Features.CustomScenario.CustomScenario NewScenario() => new()
    {
        Name = "persistence-test-" + Guid.NewGuid().ToString("N")
    };

    private static ServiceProvider CreateServices() => new ServiceCollection()
        .AddSingleton<IPluginManger, PluginMangerService>()
        .AddSingleton<IToastService, SilentToastService>()
        .BuildServiceProvider();

    private sealed class SilentToastService : IToastService
    {
        public void Init() { }
        public Task Show(string header, string text, NotificationType notificationType = NotificationType.Information,
            Window? dialogWindow = null) => Task.CompletedTask;
        public Task Show(ToastRequest request, Window? dialogWindow = null) => Task.CompletedTask;
        public IToastProgressHandle ShowProgress(string header, string text, NotificationType notificationType,
            double initialProgress = 0, bool isIndeterminate = false) => null!;
        public bool HasUnreadSuppressedNotifications() => false;
        public bool TryOpenLatestSuppressedNotification() => false;
        public bool ShowSuppressedNotificationCenter() => false;
        public void ClearUnreadSuppressedNotifications() { }
        public void Unregister() { }
    }
}
