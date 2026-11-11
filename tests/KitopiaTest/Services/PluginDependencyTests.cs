using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Messaging;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Services.Plugin;
using Kitopia.Desktop.Features.UI.UiControls.Plugin;
using Kitopia.Desktop.Features.ViewModel.Pages.plugin;
using Kitopia.Desktop.Platform.Windows;
using Kitopia.Desktop.Pages;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Media;
using Rect = Avalonia.Rect;
using HostLang = Kitopia.Feature.Localization.Lang;

namespace KitopiaTest.Services;

[TestClass]
[DoNotParallelize]
public sealed class PluginDependencyTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task OptionalDependency_EnabledFfmpegWithMissingFiles_OffersRepairUntilComplete()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(MouseHotKeyTests));
        await session.Dispatch(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "kitopia-optional-ffmpeg-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var installed = new PluginLocalInfo
            {
                PluginBaseInfo = new PluginBaseInfo { NameSign = "kitopiaffmpeg", Name = "Kitopia FFmpeg", Version = "1.0.0" }
            };
            using var services = new ServiceCollection().AddSingleton(new Ffmpeg(directory)).BuildServiceProvider();
            var plugin = new Plugin(installed);
            typeof(Plugin).GetProperty(nameof(Plugin.ServiceProvider))!.SetValue(plugin, services);
            var enabled = (IDictionary<string, Plugin>)typeof(PluginManager)
                .GetField("EnablePlugins", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            var installedField = typeof(PluginManager).GetField("AllPluginInfos", BindingFlags.NonPublic | BindingFlags.Static)!;
            var previousInstalled = installedField.GetValue(null);
            enabled.Add("kitopiaffmpeg", plugin);
            installedField.SetValue(null, new[] { installed });
            try
            {
                using var owner = new PluginInfoUiHelper { PluginBaseInfo = new PluginBaseInfo
                {
                    NameSign = "kitopiaex", OptionalDependencies = new() { ["kitopiaffmpeg"] = "[1.0.0,2.0.0)" }
                } };
                var item = owner.OptionalDependencies.Single();
                Assert.IsTrue(item.IsEnabled);
                Assert.IsFalse(item.IsAvailable);
                Assert.AreEqual("lang.kitopia.plugins.dependency_incomplete", item.StatusKey);
                Assert.AreEqual("lang.kitopia.plugins.reinstall_dependency", item.ActionKey);
                Assert.IsTrue(item.InstallCommand.CanExecute(null));
                foreach (var file in new[] { "avutil-61.dll", "swresample-7.dll", "swscale-10.dll", "avcodec-63.dll",
                             "avformat-63.dll", "avfilter-12.dll", "avdevice-63.dll", "ffmpeg.exe", "ffprobe.exe" })
                    File.WriteAllText(Path.Combine(directory, file), "fixture");
                item.Refresh();
                Assert.IsTrue(item.IsAvailable);
                Assert.IsFalse(item.InstallCommand.CanExecute(null));
                File.Delete(Path.Combine(directory, "ffprobe.exe"));
                item.Refresh();
                Assert.IsFalse(item.IsAvailable);
                Assert.IsTrue(item.InstallCommand.CanExecute(null));
            }
            finally
            {
                enabled.Remove("kitopiaffmpeg");
                installedField.SetValue(null, previousInstalled);
                Directory.Delete(directory, recursive: true);
            }
        }, CancellationToken.None);
    }

    [TestMethod]
    [DataRow("zh-CN", false)]
    [DataRow("en-US", true)]
    public async Task OptionalDependency_ManagerAndDetail_BadgeOpensPopupWithoutChangingLayout(string language, bool dark)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ImageCompressionWindowTests));
        await session.Dispatch(() =>
        {
            var previousLanguage = HostLang.Current.Language;
            HostLang.Current.UseLanguage(language);
            var manager = new PluginManagerPageViewModel();
            foreach (var existing in manager.Items) existing.Dispose();
            manager.Items.Clear();
            using var owner = new PluginInfoUiHelper
            {
                PluginBaseInfo = new PluginBaseInfo
                {
                    NameSign = "kitopiaex", Name = "KitopiaEx", Version = "1.0.0",
                    OptionalDependencies = new() { ["kitopiaffmpeg"] = "[1.0.0,2.0.0)" },
                    OptionalDependencyDescriptions = new() { ["kitopiaffmpeg"] = "lang.kitopia.plugins.ffmpeg_features" }
                },
                Description = "Kitopia extensions", AuthorName = "Kitopia", VersionDetails = [],
                Icon = new WriteableBitmap(new PixelSize(1, 1), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul),
                AuthorAvatar = new WriteableBitmap(new PixelSize(1, 1), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul)
            };
            manager.Items.Add(owner);
            var window = new Window { Width = 1000, Height = 800,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
            try
            {
                window.Show();
                foreach (Control page in new Control[]
                         {
                             new PluginManagerPage { DataContext = manager },
                             new PluginDetail { DataContext = new PluginDetailViewModel(owner) }
                         })
                {
                    window.Content = page;
                    Dispatcher.UIThread.RunJobs();
                    var badge = page.GetVisualDescendants().OfType<PluginOptionalDependenciesButton>().Single();
                    Assert.IsTrue(badge.IsEffectivelyVisible);
                    Assert.IsTrue(badge.Bounds.Height <= 22);
                    var flyout = (Flyout)badge.Flyout!;
                    Assert.IsFalse(flyout.IsOpen);
                    Assert.IsFalse(page.GetVisualDescendants().OfType<PluginDependencyView>().Any(control => control.IsEffectivelyVisible));
                    Control layout = page is PluginManagerPage
                        ? badge.GetVisualAncestors().OfType<Border>().First(border => border.Width == 390) : page;
                    var originalBounds = layout.Bounds;
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using (var collapsedFrame = window.CaptureRenderedFrame())
                    {
                        Assert.IsNotNull(collapsedFrame);
                        var collapsedScreenshot = Path.Combine(TestContext.TestRunResultsDirectory!, $"optional-popup-closed-{page.GetType().Name}-{language}-{dark}.png");
                        collapsedFrame.Save(collapsedScreenshot);
                        TestContext.AddResultFile(collapsedScreenshot);
                    }
                    badge.Focus();
                    window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                    window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                    Dispatcher.UIThread.RunJobs();
                    Assert.IsTrue(flyout.IsOpen);
                    Assert.AreEqual(originalBounds, layout.Bounds);
                    var popupContent = (Control)flyout.Content!;
                    var card = popupContent.GetVisualDescendants().OfType<PluginDependencyView>().Single();
                    Assert.IsTrue(card.IsEffectivelyVisible);
                    Assert.AreSame(owner.OptionalDependencies.Single(), card.DataContext);
                    Assert.IsTrue(card.GetVisualDescendants().OfType<TextBlock>().Any(text =>
                        text.Text == HostLang.Get("lang.kitopia.plugins.ffmpeg_features")));
                    var action = card.GetVisualDescendants().OfType<Button>().Single(button =>
                        button.Command == owner.OptionalDependencies[0].InstallCommand);
                    Assert.IsTrue(action.IsEffectivelyVisible);
                    Assert.IsTrue(action.IsEffectivelyEnabled);
                    var popupRoot = TopLevel.GetTopLevel(card)!;
                    Assert.IsTrue(new Rect(popupRoot.ClientSize).Contains(new Rect(action.TranslatePoint(default, popupRoot)!.Value, action.Bounds.Size)), page.GetType().Name);
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var frame = window.CaptureRenderedFrame();
                    Assert.IsNotNull(frame);
                    var screenshot = Path.Combine(TestContext.TestRunResultsDirectory!, $"optional-{page.GetType().Name}-{language}-{dark}.png");
                    frame.Save(screenshot);
                    TestContext.AddResultFile(screenshot);
                    flyout.Hide();
                    Dispatcher.UIThread.RunJobs();
                    Assert.IsFalse(flyout.IsOpen);
                    Assert.AreEqual(originalBounds, layout.Bounds);
                    var info = owner.PluginBaseInfo;
                    var withoutDependencies = info;
                    withoutDependencies.OptionalDependencies = [];
                    owner.PluginBaseInfo = withoutDependencies;
                    Dispatcher.UIThread.RunJobs();
                    Assert.IsFalse(badge.IsEffectivelyVisible);
                    owner.PluginBaseInfo = info;
                }
            }
            finally
            {
                window.Close();
                WeakReferenceMessenger.Default.UnregisterAll(manager);
                HostLang.Current.UseLanguage(previousLanguage);
            }
        }, CancellationToken.None);
    }

    [TestMethod]
    [DataRow("zh-CN", false)]
    [DataRow("en-US", true)]
    public async Task DependencyCard_MissingAndDownloading_ShowsFeaturesAndLiveProgress(string language, bool dark)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ImageCompressionWindowTests));
        await session.Dispatch(() =>
        {
            var previousLanguage = HostLang.Current.Language;
            HostLang.Current.UseLanguage(language);
            using var owner = new PluginInfoUiHelper
            {
                PluginBaseInfo = new PluginBaseInfo
                {
                    NameSign = "optional_ui", Name = "Optional dependency test", Version = "1.0.0",
                    OptionalDependencies = new() { ["optional_" + Guid.NewGuid().ToString("N")] = "[1.0.0,2.0.0)" }
                }
            };
            var info = owner.PluginBaseInfo;
            info.OptionalDependencyDescriptions = new() { [owner.OptionalDependencies[0].NameSign] = "lang.kitopia.plugins.ffmpeg_features" };
            owner.PluginBaseInfo = info;
            Assert.IsTrue(owner.HasOptionalDependencies);
            var item = owner.OptionalDependencies.Single();
            Assert.AreEqual("lang.kitopia.plugins.dependency_missing", item.StatusKey);
            Assert.IsTrue(item.InstallCommand.CanExecute(null));
            var view = new PluginDependencyView { DataContext = item };
            var window = new Window
            {
                Width = 390, Height = 340, Content = new Border { Padding = new Thickness(16), Child = view },
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            var progress = new PluginDownloadProgress(new PluginBaseInfo { NameSign = item.NameSign }, "1.0.0", 45, 100);
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.IsTrue(view.GetVisualDescendants().OfType<TextBlock>()
                    .Any(text => text.Text == HostLang.Get("lang.kitopia.plugins.ffmpeg_features")));
                var action = view.GetVisualDescendants().OfType<Button>().Single(button => button.Command == item.InstallCommand);
                Assert.AreEqual(HostLang.Get("lang.kitopia.plugins.install_dependency"), action.Content);
                Assert.IsTrue(action.IsEffectivelyEnabled);
                PluginManager.ReportDownloadProgress(progress);
                Dispatcher.UIThread.RunJobs();
                var bar = view.GetVisualDescendants().OfType<ProgressBar>().Single();
                Assert.IsTrue(bar.IsEffectivelyVisible);
                Assert.AreEqual(45d, bar.Value);
                Assert.IsFalse(action.IsEffectivelyEnabled);
                Assert.AreEqual("lang.kitopia.plugins.dependency_installing", item.StatusKey);
                PluginManager.ReportDownloadProgress(progress with { TotalBytes = null });
                Dispatcher.UIThread.RunJobs();
                Assert.IsTrue(bar.IsIndeterminate);
                PluginManager.ReportDownloadProgress(progress with { IsDownloading = false });
                Dispatcher.UIThread.RunJobs();
                Assert.IsFalse(bar.IsEffectivelyVisible);
                Assert.IsTrue(action.IsEffectivelyEnabled);
                Assert.IsTrue(new Rect(window.ClientSize).Contains(new Rect(action.TranslatePoint(default, window)!.Value, action.Bounds.Size)));
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = window.CaptureRenderedFrame();
                Assert.IsNotNull(frame);
                var screenshot = Path.Combine(TestContext.TestRunResultsDirectory!, $"optional-dependency-{language}-{dark}.png");
                frame.Save(screenshot);
                TestContext.AddResultFile(screenshot);
            }
            finally
            {
                window.Close();
                PluginManager.ReportDownloadProgress(progress with { IsDownloading = false });
                HostLang.Current.UseLanguage(previousLanguage);
            }
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task DependencyState_LifecycleChanges_DistinguishesDisabledIncompatibleIncompleteAndReady()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(MouseHotKeyTests));
        await session.Dispatch(async () =>
        {
            var previousServices = ServiceManager.Services;
            using var services = new ServiceCollection().AddSingleton<IHotKetImpl>(new HotKeyImpl()).BuildServiceProvider();
            ServiceManager.Services = services;
            var installed = new PluginLocalInfo
            {
                PluginBaseInfo = new PluginBaseInfo
                {
                    Name = "Dependency fixture", NameSign = "optional_" + Guid.NewGuid().ToString("N"),
                    Version = "1.0.0", Main = "PluginUnload.dll", Dependencies = []
                },
                Path = Path.Combine(AppContext.BaseDirectory, "PluginFixture") + Path.DirectorySeparatorChar,
                FullPath = Path.Combine(AppContext.BaseDirectory, "PluginFixture", "PluginUnload.dll")
            };
            var installedField = typeof(PluginManager).GetField("AllPluginInfos", BindingFlags.NonPublic | BindingFlags.Static)!;
            var previousInstalled = installedField.GetValue(null);
            installedField.SetValue(null, new[] { installed });
            var filesComplete = false;
            using var item = new PluginDependencyViewModel(installed.ToPlgString(), "[1.0.0,2.0.0)", "Description",
                checkAvailability: () => filesComplete);
            try
            {
                Assert.AreEqual("lang.kitopia.disabled", item.StatusKey);
                Assert.AreEqual("lang.kitopia.plugins.enable_dependency", item.ActionKey);
                var info = installed.PluginBaseInfo;
                info.Version = "2.0.0";
                installed.PluginBaseInfo = info;
                WeakReferenceMessenger.Default.Send(new PluginsReloaded());
                Assert.AreEqual("lang.kitopia.plugins.dependency_incompatible", item.StatusKey);
                Assert.AreEqual("lang.kitopia.plugins.install_dependency", item.ActionKey);
                info.Version = "1.0.0";
                installed.PluginBaseInfo = info;
                await PluginManager.EnableOneAsync(installed);
                WeakReferenceMessenger.Default.Send(new PluginsReloaded());
                Assert.AreEqual("lang.kitopia.plugins.dependency_incomplete", item.StatusKey);
                Assert.AreEqual("lang.kitopia.plugins.reinstall_dependency", item.ActionKey);
                Assert.IsTrue(item.CanInstall);
                filesComplete = true;
                WeakReferenceMessenger.Default.Send(new PluginsReloaded());
                Assert.AreEqual("lang.kitopia.plugins.dependency_ready", item.StatusKey);
                Assert.IsFalse(item.InstallCommand.CanExecute(null));
                Assert.IsTrue(await PluginManager.UnloadCoreAsync(installed));
                WeakReferenceMessenger.Default.Send(new PluginsReloaded());
                Assert.IsFalse(item.IsAvailable);
                Assert.AreEqual("lang.kitopia.plugins.enable_dependency", item.ActionKey);
            }
            finally
            {
                await PluginManager.UnloadCoreAsync(installed);
                installedField.SetValue(null, previousInstalled);
                ServiceManager.Services = previousServices;
            }
            return true;
        }, CancellationToken.None);
    }
}
