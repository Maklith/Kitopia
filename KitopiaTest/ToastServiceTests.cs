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

    [TestMethod]
    public void ToastDialogContent_ButtonHitTest()
    {
        var request = new ToastRequest
        {
            Header = "测试",
            Text = "描述",
            SelectionOptions = ["私有", "公开"],
            SelectedOption = "私有",
            SelectionConfirmText = "上传",
            SelectionConfirmed = _ => { }
        };
        var vm = new Kitopia.Desktop.Controls.ToastDialogContentViewModel(request);
        var control = new Kitopia.Desktop.Controls.ToastDialogContent { DataContext = vm };
        var window = new Avalonia.Controls.Window { Content = control, Width = 600, Height = 400 };
        window.Show();

        var buttons = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(control)
            .OfType<Avalonia.Controls.Button>()
            .ToList();
        foreach (var b in buttons)
        {
            Console.WriteLine($"Found button: Content='{b.Content}', Classes='{string.Join(" ", b.Classes)}', Bounds={b.Bounds}, Background={b.Background}");
            var bCp = Avalonia.VisualTree.VisualExtensions.FindDescendantOfType<Avalonia.Controls.Presenters.ContentPresenter>(b);
            Console.WriteLine($"  CP Background={bCp?.Background}, CP Bounds={bCp?.Bounds}");

            var pointOnPadding = b.TranslatePoint(new Point(5, 5), window);
            var pointOnCenter = b.TranslatePoint(new Point(b.Bounds.Width / 2, b.Bounds.Height / 2), window);
            Console.WriteLine($"  Point on padding in window: {pointOnPadding}");
            Console.WriteLine($"  Point on center in window: {pointOnCenter}");

            if (pointOnPadding.HasValue)
            {
                var visuals = Avalonia.VisualTree.VisualExtensions.GetVisualsAt(window, pointOnPadding.Value).ToList();
                Console.WriteLine($"  Visuals at padding: {string.Join(", ", visuals.Select(v => v.GetType().Name))}");
            }
            if (pointOnCenter.HasValue)
            {
                var visuals = Avalonia.VisualTree.VisualExtensions.GetVisualsAt(window, pointOnCenter.Value).ToList();
                Console.WriteLine($"  Visuals at center: {string.Join(", ", visuals.Select(v => v.GetType().Name))}");
            }
        }
    }
}
