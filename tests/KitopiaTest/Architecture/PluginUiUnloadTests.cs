using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Services.Plugin;
using Kitopia.Desktop.Features.Utils;
using Kitopia.Desktop.Platform.Windows;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace KitopiaTest.Architecture;

[TestClass]
[DoNotParallelize]
public sealed class PluginUiUnloadTests
{
    [TestMethod]
    public async Task Unload_UiAndSerializedConfig_ReclaimsContextAndPreservesOtherProperties()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(MouseHotKeyTests));
        await session.Dispatch(async () =>
        {
            var previous = ServiceManager.Services;
            using var provider = new ServiceCollection().AddSingleton<IHotKetImpl>(new HotKeyImpl()).BuildServiceProvider();
            ServiceManager.Services = provider;
            var info = new PluginLocalInfo
            {
                PluginBaseInfo = new PluginBaseInfo
                {
                    Name = "UI fixture", NameSign = "test-ui-unload-" + Guid.NewGuid().ToString("N"),
                    Version = "1.0.0", Main = "PluginUiUnload.dll", Dependencies = []
                },
                Path = Path.Combine(AppContext.BaseDirectory, "PluginFixture") + Path.DirectorySeparatorChar,
                FullPath = Path.Combine(AppContext.BaseDirectory, "PluginFixture", "PluginUiUnload.dll")
            };
            var configPath = KitopiaPaths.GetConfigFilePath(info.ToPlgString() + "#PluginUiUnload.UiConfig");
            try
            {
                for (var iteration = 0; iteration < 3; iteration++)
                {
                    var (weak, propertyId) = await LoadAsync(info);
                    // Register after the plugin so a sparse ID table cannot hide surviving higher IDs.
#pragma warning disable AVP1001 // Registration order is the regression scenario under test.
                    var hostProperty = AvaloniaProperty.Register<Control, int>("UnloadTest" + Guid.NewGuid().ToString("N"));
#pragma warning restore AVP1001
                    var hostId = GetPropertyId(hostProperty);
                    Assert.IsTrue(await PluginManager.UnloadCoreAsync(info), "UI plugin must unload on the first attempt.");
                    Assert.IsFalse(weak.IsAlive, "Success must mean the collectible UI context was reclaimed.");
                    Assert.IsNull(FindRegistered(propertyId));
                    Assert.AreSame(hostProperty, FindRegistered(hostId));
                    var control = new Control();
                    control.SetValue(hostProperty, 17);
                    Assert.AreEqual(17, control.GetValue(hostProperty));
                    Assert.AreEqual(double.PositiveInfinity, control.MaxWidth);
                }
            }
            finally
            {
                await PluginManager.UnloadCoreAsync(info);
                ServiceManager.Services = previous;
                File.Delete(configPath);
                File.Delete(configPath + ".bak");
            }
            return true;
        }, CancellationToken.None);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference Context, int PropertyId)> LoadAsync(PluginLocalInfo info)
    {
        await PluginManager.EnableOneAsync(info);
        var context = PluginManager.GetEnablePlugins()[info.ToPlgString()].AssemblyLoadContext;
        var type = context.Assembly.GetType("PluginUiUnload.PluginControl", throwOnError: true)!;
        var property = (AvaloniaProperty)type.GetField("ValueProperty")!.GetValue(null)!;
        return (new WeakReference(context, true), GetPropertyId(property));
    }

    private static int GetPropertyId(AvaloniaProperty property) => (int)typeof(AvaloniaProperty)
        .GetProperty("Id", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(property)!;

    private static object? FindRegistered(int id) => typeof(AvaloniaPropertyRegistry)
        .GetMethod("FindRegistered", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(AvaloniaPropertyRegistry.Instance, [id]);
}
