using CommunityToolkit.Mvvm.Messaging;
using Kitopia.Desktop.Features.CustomScenario;
using Kitopia.Desktop.Features.Utils;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.CustomScenario;

namespace KitopiaTest.CustomScenario;

[TestClass]
[DoNotParallelize]
public sealed class PluginScenarioIntegrationTests
{
    [TestMethod]
    public void Trigger_EmitsNameAndResolvesOnlyUnambiguousPluginKey()
    {
        var receiver = new object();
        string? received = null;
        WeakReferenceMessenger.Default.Register<string, string>(receiver, "CustomScenarioTrigger",
            (_, message) => received = message);
        try
        {
            TestTrigger.Fire("Trigger1");
            Assert.AreEqual("Trigger1", received);

            CustomScenarioGlobe.Triggers.Add("pluginA_Trigger1", new CustomScenarioTriggerInfo());
            Assert.AreEqual("pluginA_Trigger1", CustomScenarioManger.ResolveTriggerKey(received!));
            CustomScenarioGlobe.Triggers.Add("pluginB_Trigger1", new CustomScenarioTriggerInfo());
            Assert.IsNull(CustomScenarioManger.ResolveTriggerKey(received!));
            Assert.AreEqual("pluginA_Trigger1", CustomScenarioManger.ResolveTriggerKey("pluginA_Trigger1"));
        }
        finally
        {
            WeakReferenceMessenger.Default.Unregister<string, string>(receiver, "CustomScenarioTrigger");
            CustomScenarioGlobe.Triggers.Remove("pluginA_Trigger1");
            CustomScenarioGlobe.Triggers.Remove("pluginB_Trigger1");
        }
    }

    [TestMethod]
    public void Trigger_EmitsTypeAndResolvesSameNamedPluginTriggers()
    {
        var receiver = new object();
        Type? received = null;
        WeakReferenceMessenger.Default.Register<Type, string>(receiver, "CustomScenarioTrigger",
            (_, message) => received = message);
        CustomScenarioGlobe.Triggers.Add("pluginA_Trigger1", new CustomScenarioTriggerInfo
        {
            TriggerType = typeof(PluginA.Trigger1)
        });
        CustomScenarioGlobe.Triggers.Add("pluginB_Trigger1", new CustomScenarioTriggerInfo
        {
            TriggerType = typeof(PluginB.Trigger1)
        });
        try
        {
            PluginA.Trigger1.Fire();
            Assert.AreEqual(typeof(PluginA.Trigger1), received);
            Assert.AreEqual("pluginA_Trigger1", CustomScenarioManger.ResolveTriggerKey(received!));

            PluginB.Trigger1.Fire();
            Assert.AreEqual(typeof(PluginB.Trigger1), received);
            Assert.AreEqual("pluginB_Trigger1", CustomScenarioManger.ResolveTriggerKey(received!));
        }
        finally
        {
            WeakReferenceMessenger.Default.Unregister<Type, string>(receiver, "CustomScenarioTrigger");
            CustomScenarioGlobe.Triggers.Remove("pluginA_Trigger1");
            CustomScenarioGlobe.Triggers.Remove("pluginB_Trigger1");
        }
    }

    [TestMethod]
    public void IsUseThePlugin_TemporaryValueTypes_AreDetected()
    {
        var previousServices = ServiceManager.Services;
        using var provider = new ServiceCollection().AddSingleton<IPluginManger, OwnedPluginManager>()
            .BuildServiceProvider();
        ServiceManager.Services = provider;
        using var scenario = new Kitopia.Desktop.Features.CustomScenario.CustomScenario();
        try
        {
            scenario.TempValue.Add("value", new CustomScenarioValue
            {
                SerializeType = typeof(PluginOwnedValue), ShowType = typeof(string)
            });
            Assert.IsTrue(scenario.IsUseThePlugin("fixture"));

            scenario.TempValue["value"].SerializeType = typeof(string);
            scenario.TempValue["value"].ShowType = typeof(PluginOwnedValue);
            Assert.IsTrue(scenario.IsUseThePlugin("fixture"));
            Assert.IsFalse(scenario.IsUseThePlugin("other"));
        }
        finally
        {
            ServiceManager.Services = previousServices;
        }
    }

    private sealed class TestTrigger : CustomScenarioTrigger
    {
        public static void Fire(string name) => Excite(name);
    }

    private static class PluginA
    {
        public sealed class Trigger1 : CustomScenarioTrigger
        {
            public static void Fire() => Excite<Trigger1>();
        }
    }

    private static class PluginB
    {
        public sealed class Trigger1 : CustomScenarioTrigger
        {
            public static void Fire() => Excite<Trigger1>();
        }
    }

    private sealed class PluginOwnedValue;

    private sealed class OwnedPluginManager : IPluginManger
    {
        public Type GetType(string[] name) => throw new NotSupportedException();
        public PluginBaseInfo? GetPluginInfo(Type name) => null;
        public bool IsTypeFromThePlugin(Type type, string pluginName) =>
            pluginName == "fixture" && type == typeof(PluginOwnedValue);
    }
}
