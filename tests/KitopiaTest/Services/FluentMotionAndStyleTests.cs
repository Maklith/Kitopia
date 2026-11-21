using System.Reflection;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kitopia.Desktop.Controls;
using Kitopia.Desktop.Features.CustomScenario.Services;
using Kitopia.Desktop.Features.CustomScenario.ViewModels;
using Kitopia.Desktop.Features.CustomScenario.Views;
using Kitopia.Desktop.Features.Services.Plugin;
using Kitopia.Desktop.Features.UI.UiControls.Plugin;
using Kitopia.Desktop.Features.Utils;
using Kitopia.Desktop.Features.ViewModel.Pages.plugin;
using Kitopia.Desktop.ViewModels;
using Kitopia.Desktop.Windows;
using Kitopia.Feature.Localization;
using PluginCore;
using SettingsExpanderControl = Kitopia.Desktop.Controls.SettingsExpander.SettingsExpander;

namespace KitopiaTest.Services;

[TestClass]
[DoNotParallelize]
public sealed class FluentMotionAndStyleTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Kitopia.Desktop.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia();

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FluentThemes_CardsButtonsAndModalShadow_MatchDesignSpecWithTransitions(bool dark)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(FluentMotionAndStyleTests));
        await session.Dispatch(() =>
        {
            var app = Application.Current!;
            var variant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            Assert.IsNotNull(app.FindResource(variant, "KitopiaModalBoxShadow"));

            var card = new Border { Theme = (ControlTheme)app.FindResource("KitopiaCard")! };
            var pluginCard = new Border { Theme = (ControlTheme)app.FindResource("KitopiaPluginCard")!, Classes = { "interactive" } };
            var statCard = new Border { Theme = (ControlTheme)app.FindResource("KitopiaStatCard")! };
            var dangerOutline = new Button { Theme = (ControlTheme)app.FindResource("KitopiaDangerOutlineButton")!, Content = "Withdraw" };
            var dangerSubtle = new Button { Theme = (ControlTheme)app.FindResource("KitopiaDangerSubtleButton")!, Content = "Delete" };
            var badgeTag = new Button { Theme = (ControlTheme)app.FindResource("KitopiaBadgeTagButton")!, Content = "#Tag" };

            var panel = new StackPanel { Children = { card, pluginCard, statCard, dangerOutline, dangerSubtle, badgeTag } };
            var window = new Window { Content = panel, Width = 600, Height = 500, RequestedThemeVariant = variant };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                Assert.AreEqual(new CornerRadius(12), card.CornerRadius);
                Assert.AreEqual(new CornerRadius(12), pluginCard.CornerRadius);
                Assert.AreEqual(new CornerRadius(12), statCard.CornerRadius);
                Assert.AreEqual(new CornerRadius(6), dangerOutline.CornerRadius);
                Assert.AreEqual(new CornerRadius(6), dangerSubtle.CornerRadius);
                Assert.AreEqual(new CornerRadius(4), badgeTag.CornerRadius);

                Assert.IsTrue(card.Transitions?.OfType<BrushTransition>().Any(t => t.Property == Border.BackgroundProperty) == true);
                Assert.IsTrue(card.Transitions?.OfType<BoxShadowsTransition>().Any() == true);
                Assert.IsTrue(pluginCard.Transitions?.OfType<TransformOperationsTransition>().Any() == true);
                Assert.IsTrue(statCard.Transitions?.OfType<BrushTransition>().Any() == true);
                Assert.IsTrue(dangerOutline.Transitions?.OfType<TransformOperationsTransition>().Any() == true);
                Assert.IsTrue(dangerSubtle.Transitions?.OfType<TransformOperationsTransition>().Any() == true);
                Assert.IsTrue(badgeTag.Transitions?.OfType<TransformOperationsTransition>().Any() == true);
                Assert.IsFalse(HasToolTipAnimation(app.Styles), "Global ToolTip style should not attach animations.");
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task Controls_SettingsExpanderAndPinButton_HaveSmoothTransformTransitions()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(FluentMotionAndStyleTests));
        await session.Dispatch(() =>
        {
            var expander = new SettingsExpanderControl
            {
                Header = "General",
                ItemsSource = new[] { "Item 1" }
            };
            var pin = new PinButton();
            var window = new Window
            {
                Content = new StackPanel { Children = { expander, pin } },
                Width = 500,
                Height = 400
            };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var chevron = expander.GetVisualDescendants().OfType<FontIcon>()
                    .Single(icon => icon.Name == "ExpandCollapseChevron");
                Assert.IsTrue(chevron.Transitions?.OfType<TransformOperationsTransition>().Any() == true,
                    "SettingsExpander chevron must animate rotation smoothly.");

                var pinIcon = pin.GetVisualDescendants().OfType<FontIcon>()
                    .Single(icon => icon.Name == "Icon");
                Assert.IsTrue(pinIcon.Transitions is null || pinIcon.Transitions.Count == 0,
                    "PinButton icon must disable transitions when unchecked so recycled rows do not animate.");
                pin.IsChecked = true;
                Dispatcher.UIThread.RunJobs();
                Assert.IsTrue(pinIcon.Transitions is null || pinIcon.Transitions.Count == 0,
                    "PinButton icon must disable transitions when checked so recycled rows do not animate.");
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task Dialogs_DialogWindowAndOvercover_UseFluentModalRadiusAndShadow()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(FluentMotionAndStyleTests));
        await session.Dispatch(() =>
        {
            var dialogWindow = new DialogWindow(new DialogContent { Title = "Confirm", Content = "Proceed?", CloseButtonText = "Close" });
            var overcover = new DialogOvercover(new DialogContent { Title = "Overlay", Content = "Details", CloseButtonText = "OK" });
            var host = new Window { Content = overcover, Width = 800, Height = 600 };
            try
            {
                dialogWindow.Show();
                host.Show();
                Dispatcher.UIThread.RunJobs();

                var dialogBg = dialogWindow.FindControl<Border>("BackgroundElement")!;
                var overcoverBg = overcover.FindControl<Border>("BackgroundElement")!;
                Assert.IsFalse(dialogBg.ClipToBounds, "Outer modal border must not clip its BoxShadow.");
                Assert.IsFalse(overcoverBg.ClipToBounds, "Outer overcover border must not clip its BoxShadow.");
                Assert.AreEqual(new CornerRadius(16), dialogBg.CornerRadius);
                Assert.AreEqual(new CornerRadius(16), overcoverBg.CornerRadius);
                Assert.AreEqual(dialogWindow.FindResource(dialogWindow.ActualThemeVariant, "KitopiaModalBoxShadow"), dialogBg.BoxShadow);
                Assert.AreEqual(host.FindResource(host.ActualThemeVariant, "KitopiaModalBoxShadow"), overcoverBg.BoxShadow);
            }
            finally
            {
                dialogWindow.Close();
                host.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task MainWindow_PageContentChanges_CancelsPriorEntranceAndSupportsSingletonPages()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(FluentMotionAndStyleTests));
        await session.Dispatch(() =>
        {
            var window = new MainWindow();
            try
            {
                window.Show();
                window.IsVisible = true;
                Dispatcher.UIThread.RunJobs();

                var host = window.FindControl<ContentPresenter>("PageContentHost")!;
                var singletonPage = new UserControl { Content = new TextBlock { Text = "Singleton" } };
                var secondPage = new UserControl { Content = new TextBlock { Text = "Second" } };

                host.Content = singletonPage;
                Dispatcher.UIThread.RunJobs();
                host.Content = secondPage;
                Dispatcher.UIThread.RunJobs();
                host.Content = singletonPage;
                Dispatcher.UIThread.RunJobs();

                Assert.AreSame(singletonPage, host.Content);
                Assert.IsTrue(singletonPage.IsVisible);
            }
            finally
            {
                window.IsVisible = false;
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task DarkMode_PluginDetailAndSelectionTranslation_UseThemeAwareTokens()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(FluentMotionAndStyleTests));
        await session.Dispatch(() =>
        {
            var online = new OnlinePluginInfo
            {
                Name = "Sample Plugin",
                NameSign = "sampleplugin",
                DescriptionShort = "Short description",
                Description = "Detailed description",
                AuthorNickname = "Tester"
            };
            var pluginInfo = new PluginInfoUiHelper
            {
                PluginBaseInfo = online.ToPluginBaseInfo(),
                OnlinePluginInfo = online,
                AuthorName = "Tester"
            };
            var detail = new PluginDetail { DataContext = new PluginDetailViewModel(pluginInfo) };
            var window = new Window
            {
                Content = detail,
                Width = 1000,
                Height = 650,
                RequestedThemeVariant = ThemeVariant.Dark
            };
            var selectionVm = new SelectionTranslationWindowViewModel(DispatchProxy.Create<IClipboardService, NoOpProxy>())
            {
                ErrorMessage = "Failed"
            };
            var translationWindow = new SelectionTranslationWindow(selectionVm, DispatchProxy.Create<IWindowTool, NoOpProxy>())
            {
                RequestedThemeVariant = ThemeVariant.Dark
            };
            try
            {
                window.Show();
                translationWindow.Show();
                Dispatcher.UIThread.RunJobs();

                var avatarBorder = detail.GetVisualDescendants().OfType<Border>()
                    .First(b => b.Width == 72 && b.Height == 72);
                Assert.AreEqual(new CornerRadius(12), avatarBorder.CornerRadius);
                var expectedAvatarBg = (ISolidColorBrush)detail.FindResource(ThemeVariant.Dark, "KitopiaAvatarBackground")!;
                Assert.AreEqual(expectedAvatarBg.Color, ((ISolidColorBrush)avatarBorder.Background!).Color);
                Assert.AreEqual(expectedAvatarBg.Opacity, avatarBorder.Background.Opacity);

                var card = translationWindow.FindControl<Border>("TranslationCard")!;
                Assert.AreEqual(new CornerRadius(12), card.CornerRadius);
                Assert.AreEqual(translationWindow.FindResource(ThemeVariant.Dark, "KitopiaModalBoxShadow"), card.BoxShadow);
                var expectedDanger = (ISolidColorBrush)translationWindow.FindResource(ThemeVariant.Dark, "KitopiaBadgeDangerForeground")!;
                var errorText = translationWindow.GetVisualDescendants().OfType<TextBlock>()
                    .Single(text => text.Text == "Failed");
                Assert.AreEqual(expectedDanger.Color, ((ISolidColorBrush)errorText.Foreground!).Color);
            }
            finally
            {
                translationWindow.Close();
                window.Close();
                pluginInfo.Dispose();
            }
            return true;
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task ScenarioDetail_WithdrawButton_UsesDangerOutlineTheme()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(FluentMotionAndStyleTests));
        await session.Dispatch(() =>
        {
            var scenario = new ScenarioMarketItem { SourceUuid = "uuid-1", Name = "Sample" };
            var detailVm = new ScenarioDetailViewModel(scenario);
            detailVm.Releases.Add(new ScenarioRelease { Version = "1.0.0", Status = 1, CanWithdraw = true });
            var view = new ScenarioDetail { DataContext = detailVm };
            var window = new Window { Content = view, Width = 1000, Height = 750 };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var withdrawText = Lang.Get("lang.kitopia.withdraw_this_version");
                var withdrawButton = view.GetVisualDescendants().OfType<Button>()
                    .Single(btn => Equals(btn.Content, withdrawText));
                Assert.AreSame(Application.Current!.FindResource("KitopiaDangerOutlineButton"), withdrawButton.Theme);
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    private static bool HasToolTipAnimation(IEnumerable<IStyle> styles)
    {
        foreach (var style in styles)
        {
            if (style is Style s)
            {
                if (s.Selector?.ToString().Contains("ToolTip", StringComparison.Ordinal) == true
                    && s.Animations.Count > 0)
                {
                    return true;
                }
                if (HasToolTipAnimation(s.Children)) return true;
            }
            else if (style is Avalonia.Markup.Xaml.Styling.StyleInclude { Loaded: { } loaded }
                     && HasToolTipAnimation([loaded]))
            {
                return true;
            }
            else if (style is Styles nested && HasToolTipAnimation(nested))
            {
                return true;
            }
        }
        return false;
    }

    private class NoOpProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
    }
}
