using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kitopia.Desktop.Controls;
using Kitopia.Desktop.Services;
using PluginCore;
using Ursa.Controls;
using Point = Avalonia.Point;

namespace KitopiaTest;

[TestClass]
[DoNotParallelize]
public sealed class ToastDialogInteractionTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Kitopia.Desktop.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .UseSkia();

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Show_WithDialogOwner_HoverKeepsConfirmationVisibleAndClickable(bool dark)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ToastDialogInteractionTests));
        await session.Dispatch(async () =>
        {
            var window = new Window
            {
                Content = new OverlayDialogHost { IsAnimationDisabled = true }, Width = 800, Height = 600,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                string? confirmed = null;
                var completion = new ToastService().Show(new ToastRequest
                {
                    Header = "Upload scenario", Text = "Description",
                    SelectionOptions = ["Private", "Public"], SelectedOption = "Private",
                    SelectionConfirmText = "Upload", SelectionConfirmed = value => confirmed = value
                }, window);
                Dispatcher.UIThread.RunJobs();

                var dialog = window.GetVisualDescendants().OfType<ToastDialogContent>().Single();
                var button = dialog.GetVisualDescendants().OfType<Button>()
                    .Single(item => Equals(item.Content, "Upload"));
                var presenter = button.GetVisualDescendants().OfType<ContentPresenter>()
                    .Single(item => item.Name == "PART_ContentPresenter");
                var bounds = button.Bounds;
                Assert.IsTrue(bounds.Width > 0 && bounds.Height > 0);
                foreach (var position in new[]
                         {
                             new Point(3, 3), new Point(bounds.Width / 2, bounds.Height / 2),
                             new Point(14, bounds.Height / 2), new Point(bounds.Width - 3, bounds.Height - 3)
                         })
                {
                    var point = button.TranslatePoint(position, window)!.Value;
                    for (var i = 0; i < 3; i++)
                    {
                        window.MouseMove(point);
                        Dispatcher.UIThread.RunJobs();
                        Assert.IsTrue(button.IsPointerOver, $"Button lost hover at {position}.");
                        Assert.IsTrue(button.IsVisible);
                        Assert.AreEqual(bounds, button.Bounds, "Hover changed the button layout.");
                        var brush = presenter.Background as ISolidColorBrush;
                        Assert.IsNotNull(brush, $"Button background disappeared at {position}.");
                        Assert.IsTrue(brush.Color.A > 0);
                        var text = presenter.GetVisualDescendants().OfType<TextBlock>().Single();
                        Assert.IsTrue(text.IsVisible);
                        Assert.IsTrue(text.Bounds.Width > 0 && text.Bounds.Height > 0);
                        Assert.IsNotNull(text.Foreground);
                    }
                }

                var clickPoint = button.TranslatePoint(new Point(3, bounds.Height / 2), window)!.Value;
                window.MouseDown(clickPoint, MouseButton.Left);
                window.MouseUp(clickPoint, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual("Private", confirmed);
                await completion.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }
}
