using PluginCore;

namespace KitopiaTest.Architecture;

[TestClass]
public sealed class PluginSdkCompatibilityTests
{
    [TestMethod]
    public void PluginSdk_RetainsPluginCoreAssemblyIdentity()
    {
        var sdkAssembly = typeof(IPlugin).Assembly;

        Assert.AreEqual("PluginCore", sdkAssembly.GetName().Name);
        Assert.AreEqual("PluginCore.IPlugin", typeof(IPlugin).FullName);
        Assert.AreEqual("PluginCore.IPluginManger", typeof(IPluginManger).FullName);
        Assert.AreEqual("PluginCore.IToastService", typeof(IToastService).FullName);
        Assert.AreEqual("PluginCore.IScreenCapture", typeof(IScreenCapture).FullName);
        Assert.AreEqual("PluginCore.ISearchItemTool", typeof(ISearchItemTool).FullName);
    }

    [TestMethod]
    public void WindowTool_TopmostOverload_PreservesExistingSignatureAndImplementations()
    {
        var original = typeof(IWindowTool).GetMethod(nameof(IWindowTool.SetWindowTopMost),
            [typeof(IntPtr), typeof(bool)]);
        var overload = typeof(IWindowTool).GetMethod(nameof(IWindowTool.SetWindowTopMost),
            [typeof(IntPtr), typeof(bool), typeof(bool)]);

        Assert.IsNotNull(original);
        Assert.AreEqual(typeof(void), original.ReturnType);
        Assert.IsNotNull(overload);
        Assert.IsFalse(overload.IsAbstract, "Existing window service implementations must remain loadable.");
    }

    [TestMethod]
    public void PluginSdk_ExposesExistingPluginEntryContractsFromOneAssembly()
    {
        var sdkAssembly = typeof(IPlugin).Assembly;
        var contractTypes = new[]
        {
            typeof(IPlugin),
            typeof(IPluginManger),
            typeof(IToastService),
            typeof(IClipboardService),
            typeof(IScreenCapture),
            typeof(ISearchItemTool),
            typeof(IWindowTool)
        };

        Assert.IsTrue(contractTypes.All(type => ReferenceEquals(type.Assembly, sdkAssembly)));
    }
}
