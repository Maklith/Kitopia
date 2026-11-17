using PluginCore;

namespace KitopiaTest.Architecture;

[TestClass]
public sealed class PluginSdkCompatibilityTests
{
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
}
