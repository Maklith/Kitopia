using Kitopia.Desktop.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PluginCore;
using Avalonia;
using System.Collections;
using System.Reflection;

namespace KitopiaTest;

[TestClass]
public sealed class ToastServiceTests
{
    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        if (Application.Current is null)
        {
            AppBuilder.Configure<Kitopia.Desktop.App>()
                .UsePlatformDetect()
                .SetupWithoutStarting();
        }
    }

    [TestMethod]
    public async Task Show_WhenUiDispatcherUnavailable_StillInvokesCloseAction()
    {
        var service = new ToastService();
        var closeActionCalled = false;

        await service.Show(new ToastRequest
        {
            Header = "header",
            Text = "text",
            CloseAction = () => closeActionCalled = true
        });

        Assert.IsTrue(closeActionCalled);
    }

    [TestMethod]
    public async Task Show_InUiEnvironment_InvokesCloseActionAfterToastRemoved()
    {
        var service = new ToastService();
        service.Init();

        var closeActionCalled = false;
        var itemCountWhenCloseActionRuns = -1;

        await service.Show(new ToastRequest
        {
            Header = "header",
            Text = "text",
            AutoCloseDelay = TimeSpan.FromMilliseconds(20),
            CloseAction = () =>
            {
                closeActionCalled = true;
                itemCountWhenCloseActionRuns = GetActiveToastCount(service);
            }
        });

        Assert.IsTrue(closeActionCalled);
        Assert.AreEqual(0, itemCountWhenCloseActionRuns);
    }

    private static int GetActiveToastCount(ToastService service)
    {
        var field = typeof(ToastService).GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field);
        var value = field.GetValue(service) as IDictionary;
        Assert.IsNotNull(value);
        return value.Count;
    }
}
