using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Messaging;
using Kitopia.Desktop.Features.Services.Plugin;
using Kitopia.Desktop.Features.UI.UiControls.Plugin;
using Kitopia.Desktop.Features.ViewModel.Pages.plugin;
using Kitopia.Desktop.Pages;
using PluginCore;
using Rect = Avalonia.Rect;

namespace KitopiaTest.Services;

[TestClass]
[DoNotParallelize]
public sealed class PluginDownloadProgressTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(480, false)]
    [DataRow(900, false)]
    [DataRow(480, true)]
    [DataRow(900, true)]
    public async Task MarketPluginCard_DownloadProgress_StaysInsideCardAndSeparatesStatusText(int width, bool dark)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(MouseHotKeyTests));
        await session.Dispatch(() =>
        {
            var items = new List<PluginInfoUiHelper>();
            for (var index = 0; index < 2; index++)
            {
                var online = new OnlinePluginInfo
                {
                    NameSign = "layout_" + Guid.NewGuid().ToString("N"),
                    Name = index == 0 ? "Onnx CUDA推理环境" : "Onnx OpenVino推理环境",
                    LastVersion = "1.0.0", DescriptionShort = "官方拓展插件，提供推理环境",
                    AuthorNickname = index == 0 ? "Maklith with a long author name" : "Maklith",
                    DownloadCounts = 123456789, AvailablePlatforms = ["windows"],
                    Updatetime = new DateTime(2026, 10, 4)
                };
                items.Add(new PluginInfoUiHelper
                {
                    PluginBaseInfo = online.ToPluginBaseInfo(), OnlinePluginInfo = online, IsLocal = false,
                    AuthorName = online.AuthorNickname,
                    Icon = new WriteableBitmap(new PixelSize(1, 1), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul),
                    AuthorAvatar = new WriteableBitmap(new PixelSize(1, 1), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul)
                });
            }
            var progress = new PluginDownloadProgress(items[0].PluginBaseInfo, "1.0.0", 55, 100);
            PluginManager.ReportDownloadProgress(progress);
            var templateSource = new MarketPage().FindControl<ItemsControl>("PluginCards")!;
            var cards = new ItemsControl { ItemsSource = items, ItemTemplate = templateSource.ItemTemplate, ItemsPanel = templateSource.ItemsPanel };
            var window = new Window
            {
                Width = width, Height = width < 800 ? 540 : 300,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
                Background = new SolidColorBrush(Color.Parse(dark ? "#181818" : "#F5F6F8")),
                Content = new Border { Padding = new Thickness(20), Child = cards }
            };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = window.CaptureRenderedFrame();
                Assert.IsNotNull(frame);
                var screenshot = Path.Combine(TestContext.TestRunResultsDirectory!, $"plugin-download-market-{width}-{dark}.png");
                frame.Save(screenshot);
                TestContext.AddResultFile(screenshot);

                var bar = cards.GetVisualDescendants().OfType<ProgressBar>().Single(control => control.IsEffectivelyVisible);
                var card = bar.GetVisualAncestors().OfType<Border>().First(control => control.Width == 390);
                var barBounds = new Rect(bar.TranslatePoint(default, card)!.Value, bar.Bounds.Size);
                Assert.IsTrue(new Rect(card.Bounds.Size).Contains(barBounds), $"Progress bounds {barBounds} exceed card bounds {card.Bounds.Size}; MinWidth={bar.MinWidth}.");
                Assert.IsTrue(bar.Bounds.Height <= 4);
                var status = card.GetVisualDescendants().OfType<TextBlock>().Single(control => control.Text == "下载中 55%");
                var statusBounds = new Rect(status.TranslatePoint(default, card)!.Value, status.Bounds.Size);
                Assert.IsFalse(statusBounds.Intersects(barBounds), "Status text must not overlay the progress track.");
                var author = card.GetVisualDescendants().OfType<TextBlock>().Single(control => control.Text == items[0].AuthorName);
                var authorBounds = new Rect(author.TranslatePoint(default, card)!.Value, author.Bounds.Size);
                Assert.IsFalse(authorBounds.Intersects(statusBounds));
                var downloadCount = card.GetVisualDescendants().OfType<TextBlock>().Single(control => control.Text == items[0].DownloadCountText);
                var countBounds = new Rect(downloadCount.TranslatePoint(default, card)!.Value, downloadCount.Bounds.Size);
                var version = card.GetVisualDescendants().OfType<TextBlock>().Single(control => control.Text == items[0].VersionAndDateText);
                var versionBounds = new Rect(version.TranslatePoint(default, card)!.Value, version.Bounds.Size);
                Assert.AreEqual(versionBounds.Top, countBounds.Top);
                Assert.IsTrue(countBounds.Bottom <= Math.Min(authorBounds.Top, statusBounds.Top));
                Assert.IsTrue(new Rect(card.Bounds.Size).Contains(countBounds));
                Assert.IsTrue(new Rect(card.Bounds.Size).Contains(statusBounds));
                Assert.IsTrue(bar.Foreground is ISolidColorBrush fill && fill.Color == Color.Parse(dark ? "#4CC2FF" : "#0064FA"));

                foreach (var next in new[]
                         {
                             progress with { DownloadedBytes = 0 },
                             progress with { DownloadedBytes = 1048576000, TotalBytes = null },
                             progress with { DownloadedBytes = 100, IsInstalling = true }
                         })
                {
                    PluginManager.ReportDownloadProgress(next);
                    Dispatcher.UIThread.RunJobs();
                    Assert.AreEqual(barBounds, new Rect(bar.TranslatePoint(default, card)!.Value, bar.Bounds.Size));
                    Assert.IsTrue(new Rect(card.Bounds.Size).Contains(new Rect(status.TranslatePoint(default, card)!.Value, status.Bounds.Size)));
                }
            }
            finally
            {
                window.Close();
                PluginManager.ReportDownloadProgress(progress with { IsDownloading = false });
                foreach (var item in items) item.Dispose();
            }
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task DownloadProgress_RecreatedPages_ObserveActiveDownloadAndRemoveFailedInstallation()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(MouseHotKeyTests));
        await session.Dispatch(() =>
        {
            var info = new PluginBaseInfo
            {
                NameSign = "progress_" + Guid.NewGuid().ToString("N"), Name = "Progress test",
                Version = "1.0.0", Description = "Download test"
            };
            var progress = new PluginDownloadProgress(info, "1.0.0", 25, 100);
            using var marketItem = new PluginInfoUiHelper { PluginBaseInfo = info };
            var originalPage = new PluginManagerPageViewModel();
            PluginManagerPageViewModel? reopenedPage = null;
            try
            {
                PluginManager.ReportDownloadProgress(progress);
                Assert.IsTrue(marketItem.IsDownloading);
                Assert.AreEqual(25d, marketItem.Download!.Percentage);
                Assert.AreEqual(1, originalPage.Items.Count(item => item.PluginBaseInfo.NameSign == info.NameSign));

                using var recreatedMarketItem = new PluginInfoUiHelper { PluginBaseInfo = info };
                reopenedPage = new PluginManagerPageViewModel();
                var reopenedDetail = new PluginDetailViewModel(recreatedMarketItem);
                var localItem = reopenedPage.Items.Single(item => item.PluginBaseInfo.NameSign == info.NameSign);
                Assert.IsNull(localItem.PluginLocalInfo);
                Assert.IsTrue(localItem.IsDownloading);
                Assert.AreEqual(25d, reopenedDetail.PluginInfo!.Download!.Percentage);
                Assert.IsFalse(localItem.CanRemove);
                Assert.IsFalse(localItem.CanSwitch);

                progress = progress with { DownloadedBytes = 75 };
                PluginManager.ReportDownloadProgress(progress);
                Assert.AreEqual(75d, marketItem.Download!.Percentage);
                Assert.AreEqual(75d, localItem.Download!.Percentage);
                Assert.AreEqual(75d, recreatedMarketItem.Download!.Percentage);

                progress = progress with { DownloadedBytes = 100, IsInstalling = true };
                PluginManager.ReportDownloadProgress(progress);
                Assert.AreEqual(100d, recreatedMarketItem.Download!.Percentage);
                Assert.AreEqual("正在安装", localItem.Download!.StatusText);

                PluginManager.ReportDownloadProgress(progress with { IsDownloading = false });
                Assert.IsFalse(marketItem.IsDownloading);
                Assert.IsNull(recreatedMarketItem.Download);
                Assert.IsFalse(originalPage.Items.Any(item => item.PluginBaseInfo.NameSign == info.NameSign));
                Assert.IsFalse(reopenedPage.Items.Any(item => item.PluginBaseInfo.NameSign == info.NameSign));
            }
            finally
            {
                PluginManager.ReportDownloadProgress(progress with { IsDownloading = false });
                WeakReferenceMessenger.Default.UnregisterAll(originalPage);
                foreach (var item in originalPage.Items) item.Dispose();
                if (reopenedPage is not null)
                {
                    WeakReferenceMessenger.Default.UnregisterAll(reopenedPage);
                    foreach (var item in reopenedPage.Items) item.Dispose();
                }
            }
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task PluginManagerPage_UninstalledDownload_ShowsProgressAndUpdatesUnknownSize()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(MouseHotKeyTests));
        await session.Dispatch(() =>
        {
            var info = new PluginBaseInfo
            {
                NameSign = "progress_" + Guid.NewGuid().ToString("N"), Name = "Progress test",
                Version = "1.0.0", Description = "Download test"
            };
            var progress = new PluginDownloadProgress(info, "1.0.0", 25, 100);
            PluginManager.ReportDownloadProgress(progress);
            var viewModel = new PluginManagerPageViewModel();
            var item = viewModel.Items.Single(item => item.PluginBaseInfo.NameSign == info.NameSign);
            item.OnlinePluginInfo = new OnlinePluginInfo { NameSign = info.NameSign, Name = info.Name, LastVersion = info.Version };
            item.AuthorName = "Test author";
            item.Icon = new WriteableBitmap(new PixelSize(1, 1), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            item.AuthorAvatar = new WriteableBitmap(new PixelSize(1, 1), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            var page = new PluginManagerPage { DataContext = viewModel };
            var window = new Window { Width = 850, Height = 500, Content = page };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var bar = page.GetVisualDescendants().OfType<ProgressBar>().Single(control => control.IsEffectivelyVisible);
                Assert.AreEqual(25d, bar.Value);
                Assert.IsFalse(bar.IsIndeterminate);
                Assert.IsTrue(bar.IsEffectivelyEnabled);
                Assert.IsTrue(bar.Bounds.Width > 0);
                Assert.IsTrue(page.GetVisualDescendants().OfType<TextBlock>().Any(control => control.IsEffectivelyVisible && control.Text == "下载中 25%"));
                Assert.IsFalse(page.GetVisualDescendants().OfType<Button>().Any(control => control.IsEffectivelyVisible && control.Content is "删除" or "配置" or "更新"));
                Assert.IsFalse(page.GetVisualDescendants().OfType<TextBlock>().Any(control => control.IsEffectivelyVisible &&
                    control.Text is "插件动态卸载失败，重启后完成卸载/更新" or "插件加载失败"),
                    "A download without local installation info must not show failure overlays.");
                Assert.IsFalse(page.GetVisualDescendants().OfType<Button>().Any(control => control.IsEffectivelyVisible && control.Content is "重启"));

                PluginManager.ReportDownloadProgress(progress with { DownloadedBytes = 1048576, TotalBytes = null });
                Dispatcher.UIThread.RunJobs();
                Assert.IsTrue(bar.IsIndeterminate);
                Assert.IsTrue(page.GetVisualDescendants().OfType<TextBlock>().Any(control => control.IsEffectivelyVisible && control.Text == "下载中 1.0 MB"));

                var localInfo = new PluginLocalInfo { PluginBaseInfo = info, LoadFailedReason = "Test failure" };
                item.PluginLocalInfo = localInfo;
                var installedPage = new PluginManagerPage { DataContext = viewModel };
                window.Content = installedPage;
                Dispatcher.UIThread.RunJobs();
                var unloadFailure = installedPage.GetVisualDescendants().OfType<TextBlock>()
                    .Single(control => control.Text == "插件动态卸载失败，重启后完成卸载/更新");
                var loadFailure = installedPage.GetVisualDescendants().OfType<TextBlock>()
                    .Single(control => control.Text == "插件加载失败");
                Assert.IsFalse(unloadFailure.IsEffectivelyVisible);
                Assert.IsFalse(loadFailure.IsEffectivelyVisible);

                localInfo.UnloadFailed = true;
                Dispatcher.UIThread.RunJobs();
                Assert.IsTrue(unloadFailure.IsEffectivelyVisible);
                Assert.IsFalse(loadFailure.IsEffectivelyVisible);
                localInfo.UnloadFailed = false;
                localInfo.LoadFailed = true;
                Dispatcher.UIThread.RunJobs();
                Assert.IsFalse(unloadFailure.IsEffectivelyVisible);
                Assert.IsTrue(loadFailure.IsEffectivelyVisible);
                item.PluginLocalInfo = null;

                item.Description = "Download test";
                item.VersionDetails = [new VersionDetail { Id = 1, Version = info.Version, CanDownload = true }];
                var detail = new PluginDetail { DataContext = new PluginDetailViewModel(item) };
                window.Content = detail;
                Dispatcher.UIThread.RunJobs();
                var detailBar = detail.GetVisualDescendants().OfType<ProgressBar>().Single(control => control.IsEffectivelyVisible);
                Assert.IsTrue(detailBar.IsIndeterminate);
                Assert.IsFalse(detail.GetVisualDescendants().OfType<Button>().Any(control => control.IsEffectivelyVisible && control.Content is "一键安装到 Kitopia"));

                PluginManager.ReportDownloadProgress(progress with { DownloadedBytes = 50 });
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(50d, detailBar.Value);
                Assert.IsFalse(detailBar.IsIndeterminate);

                PluginManager.ReportDownloadProgress(progress with { IsDownloading = false });
                Dispatcher.UIThread.RunJobs();
                Assert.IsTrue(detail.GetVisualDescendants().OfType<Button>().Any(control => control.IsEffectivelyVisible && control.Content is "一键安装到 Kitopia"));
                Assert.IsFalse(detailBar.IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
                PluginManager.ReportDownloadProgress(progress with { IsDownloading = false });
                WeakReferenceMessenger.Default.UnregisterAll(viewModel);
                foreach (var existing in viewModel.Items) existing.Dispose();
            }
        }, CancellationToken.None);
    }
}
