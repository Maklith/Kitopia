using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Kitopia.Desktop.Abstractions.Shell;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Services.Onnx;
using Kitopia.Desktop.Features.Services.Plugin;
using Kitopia.Desktop.Features.ViewModel.Pages;
using Kitopia.Desktop.Pages;
using Kitopia.Feature.Localization;
using PluginCore.Config;
using PluginCore.Onnx;

namespace KitopiaTest.Services;

[TestClass]
[DoNotParallelize]
public sealed class OnnxRuntimeAvailabilityTests
{
    private const string Source = "onnx-availability-test";
    public TestContext TestContext { get; set; } = null!;

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Kitopia.Desktop.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia();

    [TestCleanup]
    public void Cleanup()
    {
        PluginOverall.OnnxRuntimes.Remove(Source);
        PluginOverall.OnnxModelInfos.Remove(Source);
    }

    [TestMethod]
    public void CheckAvailability_Cpu_ExecutesProbeWithoutReplacingSession()
    {
        using var session = new OnnxRuntime.CPU.MInferenceSession();
        Assert.AreEqual(true, session.CheckAvailability());
        Assert.AreEqual(true, session.CheckAvailability());
        Assert.HasCount(0, session.InputNames);
        Assert.HasCount(0, session.OutputShape);
    }

    [TestMethod]
    public void CheckAvailability_LegacyPlugin_ReturnsUnknown()
    {
        using IInferenceSession session = new LegacySession();
        Assert.IsNull(session.CheckAvailability());
    }

    [TestMethod]
    public async Task Refresh_FailingFactoryDeviceAndLegacyPlugin_ShowsDiagnosticsAndDisposesProbes()
    {
        var probes = new List<ProbeSession>();
        var failing = true;
        PluginOverall.OnnxRuntimes[Source] = new()
        {
            ["test-good"] = () => AddProbe(() => true),
            ["GPU(CUDA)"] = () => AddProbe(() => failing ? throw new DllNotFoundException("cudnn64_9.dll is missing") : true),
            ["test-factory"] = () => throw new TypeInitializationException("Native", new DllNotFoundException("native library is missing")),
            ["test-legacy"] = () => new LegacySession()
        };
        var config = new TestConfigService();
        config.Config.OnnxTargetDevices["saved-model"] = "test-removed";
        var shell = new TestShell();
        var viewModel = new OnnxModelManagerPageViewModel(config, shell, new TestRuntimeProbe());

        await viewModel.RefreshCommand.ExecuteAsync(false);
        var states = viewModel.Runtimes.ToDictionary(runtime => runtime.Device);
        Assert.AreEqual(true, states["test-good"].IsAvailable);
        Assert.AreEqual(false, states["GPU(CUDA)"].IsAvailable);
        Assert.AreEqual("cudnn64_9.dll is missing", states["GPU(CUDA)"].Error);
        Assert.AreEqual("native library is missing", states["test-factory"].Error);
        Assert.IsNull(states["test-legacy"].IsAvailable);
        Assert.IsTrue(states["test-legacy"].CanSelect);
        Assert.IsFalse(states["test-removed"].CanSelect);
        Assert.AreEqual("lang.kitopia.onnx.runtime_not_installed", states["test-removed"].Error);
        Assert.IsTrue(probes.All(probe => probe.Disposed));
        Assert.IsTrue(states.Values.All(runtime => !runtime.IsChecking));
        Assert.AreEqual(0, config.SaveCount);
        Assert.AreEqual("test-removed", config.Config.OnnxTargetDevices["saved-model"]);

        viewModel.OpenDocumentationCommand.Execute(states["GPU(CUDA)"]);
        Assert.AreEqual(states["GPU(CUDA)"].DocumentationUrl, shell.LastOpened);
        viewModel.OpenDriversCommand.Execute(states["GPU(CUDA)"]);
        Assert.AreEqual(states["GPU(CUDA)"].DriverUrl, shell.LastOpened);

        failing = false;
        await viewModel.RefreshCommand.ExecuteAsync(true);
        var repaired = viewModel.Runtimes.Single(runtime => runtime.Device == "GPU(CUDA)");
        Assert.AreEqual(true, repaired.IsAvailable);
        Assert.IsNull(repaired.Error);
        Assert.IsTrue(repaired.CanSelect);
        Assert.IsTrue(probes.All(probe => probe.Disposed));

        ProbeSession AddProbe(Func<bool?> check)
        {
            var probe = new ProbeSession(check);
            probes.Add(probe);
            return probe;
        }
    }

    [TestMethod]
    public void SelectRuntime_CheckingUnavailableAndMissingModel_DoesNotSaveUntilSelectable()
    {
        var path = Path.GetTempFileName();
        try
        {
            var config = new TestConfigService();
            var available = new OnnxRuntimeStatus("CPU") { IsChecking = false, IsAvailable = true };
            var unavailable = new OnnxRuntimeStatus("GPU(CUDA)") { IsChecking = false, IsAvailable = false };
            var checking = new OnnxRuntimeStatus("GPU(OpenVino)");
            var model = new OnnxModelInfoWrapper
            {
                PluginStr = "Kitopia",
                Model = new OnnxModelInfo { SignName = "test-model", ModelPath = path }
            };
            config.Config.OnnxTargetDevices[model.Model.SignName] = unavailable.Device;
            var row = new OnnxModelRuntimeSelection(model, [available, unavailable, checking], config);
            row.SelectRuntimeCommand.Execute(unavailable);
            row.SelectRuntimeCommand.Execute(checking);
            Assert.AreEqual(unavailable.Device, row.CurrentDevice);
            Assert.AreEqual(0, config.SaveCount);

            row.SelectRuntimeCommand.Execute(available);
            row.SelectRuntimeCommand.Execute(available);
            Assert.AreEqual("CPU", row.CurrentDevice);
            Assert.AreEqual("CPU", config.Config.OnnxTargetDevices[model.Model.SignName]);
            Assert.AreEqual(1, config.SaveCount);

            row.SelectedRuntime = unavailable;
            row.SelectedRuntime = checking;
            row.SelectedRuntime = null;
            row.SelectedRuntime = new OnnxRuntimeStatus("CPU") { IsChecking = false, IsAvailable = true };
            Assert.AreSame(available, row.SelectedRuntime);
            Assert.AreEqual(1, config.SaveCount);

            checking.IsChecking = false;
            checking.IsAvailable = true;
            model.Model.ModelPath = path + ".missing";
            row.SelectRuntimeCommand.Execute(checking);
            Assert.AreEqual("CPU", row.CurrentDevice);
            Assert.AreEqual(1, config.SaveCount);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task Refresh_CanceledDuringProbe_DisposesAndStopsWithoutThrowing()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new ProbeSession(() =>
        {
            started.SetResult();
            finish.Task.GetAwaiter().GetResult();
            return true;
        });
        var laterInvocations = 0;
        PluginOverall.OnnxRuntimes[Source] = new()
        {
            ["test-blocking"] = () => probe,
            ["test-later"] = () => { laterInvocations++; return new LegacySession(); }
        };
        var viewModel = new OnnxModelManagerPageViewModel(new TestConfigService(), new TestShell(), new TestRuntimeProbe());
        var task = viewModel.RefreshCommand.ExecuteAsync(false);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            viewModel.RefreshCommand.Cancel();
        }
        finally { finish.TrySetResult(); }
        await task;
        Assert.IsTrue(probe.Disposed);
        Assert.AreEqual(0, laterInvocations);
    }

    [TestMethod]
    public void GetSession_InitializationFails_DisposesRuntime()
    {
        var path = Path.GetTempFileName();
        var originalConfigs = ConfigManger.Configs;
        var probe = new ProbeSession(() => true);
        try
        {
            ConfigManger.Configs = new Dictionary<string, ConfigBase>
            {
                ["KitopiaConfig"] = new KitopiaConfig { OnnxTargetDevices = new() { ["test-init"] = "test-init-runtime" } }
            };
            PluginOverall.OnnxModelInfos[Source] = [new OnnxModelInfoWrapper
            {
                Model = new OnnxModelInfo { SignName = "test-init", ModelPath = path }
            }];
            PluginOverall.OnnxRuntimes[Source] = new() { ["test-init-runtime"] = () => probe };
            Assert.ThrowsExactly<NotSupportedException>(() => new InferenceSessionManager().GetSession("test-init"));
            Assert.IsTrue(probe.Disposed);
        }
        finally
        {
            ConfigManger.Configs = originalConfigs;
            File.Delete(path);
        }
    }

    [TestMethod]
    [DataRow("zh-CN", 900, false, 3)]
    [DataRow("en-US", 620, false, 2)]
    [DataRow("zh-CN", 620, true, 2)]
    [DataRow("en-US", 1560, false, 5)]
    [DataRow("zh-CN", 1560, false, 5)]
    [DataRow("en-US", 380, true, 1)]
    public async Task ModelManager_DiagnosticsAndBackendSelection_RenderAndUpdate(string language, int width, bool dark, int columns)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(OnnxRuntimeAvailabilityTests));
        await session.Dispatch(async () =>
        {
            var originalLanguage = Lang.Current.Language;
            var path = Path.GetTempFileName();
            var screenshotDirectory = Path.Combine(AppContext.BaseDirectory, "onnx-availability-screenshots");
            Directory.CreateDirectory(screenshotDirectory);
            var config = new TestConfigService();
            config.Config.OnnxTargetDevices["test-ui"] = "GPU(CUDA)";
            PluginOverall.OnnxRuntimes[Source] = new()
            {
                ["CPU"] = () => new ProbeSession(() => true),
                ["GPU(CUDA)"] = () => new ProbeSession(() => throw new DllNotFoundException("cudnn64_9.dll is missing")),
                ["CPU(OpenVino)"] = () => new ProbeSession(() => true),
                ["GPU(OpenVino)"] = () => new ProbeSession(() => false),
                ["NPU(OpenVino)"] = () => new ProbeSession(() => throw new InvalidOperationException("No NPU device was found"))
            };
            PluginOverall.OnnxModelInfos[Source] = [new OnnxModelInfoWrapper
            {
                PluginStr = "Kitopia",
                Model = new OnnxModelInfo { Name = "EmbeddingGemma 2", SignName = "test-ui", ModelPath = path, IsBundled = true }
            }];
            Lang.Current.UseLanguage(language);
            var shell = new TestShell();
            var viewModel = new OnnxModelManagerPageViewModel(config, shell, new TestRuntimeProbe());
            var page = new OnnxModelManagerPage { DataContext = viewModel };
            var window = new Window { Content = page, Width = width, Height = 900,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
            try
            {
                window.Show();
                await viewModel.RefreshCommand.ExecutionTask!;
                Assert.AreEqual(true, page.FindControl<Button>("RecheckRuntimesButton")!.CommandParameter);
                Dispatcher.UIThread.RunJobs();
                var row = viewModel.Models.Single(model => model.ModelInfo.Model.SignName == "test-ui");
                var grid = page.FindControl<DataGrid>("ModelsGrid")!;
                grid.ScrollIntoView(row, grid.Columns[3]);
                var scroll = page.FindControl<ScrollViewer>("PageScroll")!;
                scroll.Offset = new Vector(0, scroll.Extent.Height);
                Dispatcher.UIThread.RunJobs();
                var picker = page.GetVisualDescendants().OfType<ComboBox>()
                    .Single(control => ReferenceEquals(control.DataContext, row));
                Assert.HasCount(5, picker.Items);
                Assert.AreSame(row.Runtimes.Single(runtime => runtime.Device == "GPU(CUDA)"), picker.SelectedItem);
                Assert.AreEqual(0, config.SaveCount);
                picker.Focus();
                window.KeyPress(Key.F4, RawInputModifiers.None, PhysicalKey.F4, "");
                window.KeyRelease(Key.F4, RawInputModifiers.None, PhysicalKey.F4, "");
                Dispatcher.UIThread.RunJobs();
                Assert.IsTrue(picker.IsDropDownOpen);
                for (var index = 0; index < picker.Items.Count; index++)
                {
                    var runtime = (OnnxRuntimeStatus)picker.Items[index]!;
                    var item = (ComboBoxItem)picker.ContainerFromIndex(index)!;
                    Assert.AreEqual(runtime.CanSelect, item.IsEnabled);
                }
                var cpuItem = (ComboBoxItem)picker.ContainerFromIndex(0)!;
                cpuItem.Focus();
                var popup = TopLevel.GetTopLevel(cpuItem)!;
                popup.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                popup.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual("CPU", row.CurrentDevice);
                Assert.AreSame(row.Runtimes.Single(runtime => runtime.Device == "CPU"), picker.SelectedItem);
                Assert.IsFalse(picker.IsDropDownOpen);
                Assert.AreEqual(1, config.SaveCount);

                var modelRows = grid.GetVisualDescendants().OfType<DataGridRow>().ToArray();
                Assert.IsTrue(modelRows.All(modelRow => modelRow.Bounds.Height <= 81));
                if (width >= 1560)
                {
                    Assert.IsTrue(grid.Columns[1].ActualWidth > grid.Columns[0].ActualWidth);
                    Assert.IsTrue(grid.Columns[0].ActualWidth < grid.Bounds.Width * 0.4);
                }

                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using (var modelScreenshot = window.CaptureRenderedFrame())
                {
                    Assert.IsNotNull(modelScreenshot);
                    var modelScreenshotPath = Path.Combine(screenshotDirectory, $"onnx-models-{language}-{width}-{dark}.png");
                    modelScreenshot.Save(modelScreenshotPath);
                    TestContext.AddResultFile(modelScreenshotPath);
                }
                scroll.Offset = default;

                var texts = page.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray();
                Assert.Contains(Lang.Get("lang.kitopia.onnx.unavailable"), texts);
                var detailsButtons = page.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.Classes.Contains("runtimeDetails")).ToArray();
                var cpuDetails = detailsButtons.Single(button => button.DataContext is OnnxRuntimeStatus { Device: "CPU" });
                Assert.IsFalse(cpuDetails.IsVisible);
                Dispatcher.UIThread.RunJobs();
                var runtimeCards = page.FindControl<ItemsControl>("RuntimeCards")!;
                var cardBounds = runtimeCards.GetVisualDescendants().OfType<Border>()
                    .Where(border => border.Classes.Contains("runtimeCard"))
                    .Select(border => new Rect(border.TranslatePoint(default, runtimeCards)!.Value, border.Bounds.Size))
                    .ToArray();
                Assert.HasCount(5, cardBounds);
                Assert.AreEqual(columns, cardBounds.Select(bounds => Math.Round(bounds.X)).Distinct().Count());
                Assert.IsTrue(cardBounds.All(bounds => bounds.X >= 0 && bounds.Right <= runtimeCards.Bounds.Width + 1));
                Assert.IsTrue(cardBounds.All(bounds => bounds.Width > 0 && bounds.Width <= 272));
                Assert.HasCount(1, cardBounds.Select(bounds => bounds.Height).Distinct());
                for (var index = 0; index < cardBounds.Length; index++)
                    for (var other = index + 1; other < cardBounds.Length; other++)
                        Assert.IsFalse(cardBounds[index].Intersects(cardBounds[other]), "Runtime cards must not overlap.");
                if (width == 1560)
                {
                    window.Width = 900;
                    Dispatcher.UIThread.RunJobs();
                    var resizedColumns = runtimeCards.GetVisualDescendants().OfType<Border>()
                        .Where(border => border.Classes.Contains("runtimeCard"))
                        .Select(border => Math.Round(border.TranslatePoint(default, runtimeCards)!.Value.X)).Distinct();
                    Assert.HasCount(3, resizedColumns);
                    window.Width = width;
                    Dispatcher.UIThread.RunJobs();
                }
                var cudaDetails = detailsButtons.Single(button => button.DataContext is OnnxRuntimeStatus { Device: "GPU(CUDA)" });
                cudaDetails.Focus();
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                Dispatcher.UIThread.RunJobs();
                var flyout = (Flyout)cudaDetails.Flyout!;
                Assert.IsTrue(flyout.IsOpen);
                var diagnostics = (Control)flyout.Content!;
                Assert.Contains(Lang.Get("lang.kitopia.onnx.cuda_requirements"),
                    diagnostics.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));
                Assert.AreEqual("cudnn64_9.dll is missing",
                    diagnostics.GetVisualDescendants().OfType<SelectableTextBlock>().Single().Text);
                var guide = diagnostics.GetVisualDescendants().OfType<Button>().Single(button =>
                    Equals(button.Content, Lang.Get("lang.kitopia.onnx.official_guide")));
                Assert.AreSame(viewModel.OpenDocumentationCommand, guide.Command);
                guide.Command!.Execute(guide.CommandParameter);
                Assert.AreEqual(((OnnxRuntimeStatus)cudaDetails.DataContext!).DocumentationUrl, shell.LastOpened);
                var driver = diagnostics.GetVisualDescendants().OfType<Button>().Single(button =>
                    Equals(button.Content, Lang.Get("lang.kitopia.onnx.drivers")));
                Assert.AreSame(viewModel.OpenDriversCommand, driver.Command);
                driver.Command!.Execute(driver.CommandParameter);
                Assert.AreEqual(((OnnxRuntimeStatus)cudaDetails.DataContext!).DriverUrl, shell.LastOpened);
                Assert.AreEqual(cardBounds[0].Height,
                    runtimeCards.GetVisualDescendants().OfType<Border>()
                        .Single(border => border.Classes.Contains("runtimeCard") && border.DataContext is OnnxRuntimeStatus { Device: "GPU(CUDA)" }).Bounds.Height);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using (var detailsScreenshot = window.CaptureRenderedFrame())
                {
                    Assert.IsNotNull(detailsScreenshot);
                    var detailsScreenshotPath = Path.Combine(screenshotDirectory, $"onnx-details-{language}-{width}-{dark}.png");
                    detailsScreenshot.Save(detailsScreenshotPath);
                    TestContext.AddResultFile(detailsScreenshotPath);
                }
                flyout.Hide();
            }
            finally
            {
                page.FindControl<ScrollViewer>("PageScroll")!.Offset = default;
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var screenshot = window.CaptureRenderedFrame();
                Assert.IsNotNull(screenshot);
                var screenshotPath = Path.Combine(screenshotDirectory, $"onnx-runtimes-{language}-{width}-{dark}.png");
                screenshot.Save(screenshotPath);
                TestContext.AddResultFile(screenshotPath);
                window.Close();
                Lang.Current.UseLanguage(originalLanguage);
                File.Delete(path);
            }
            return true;
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task Refresh_UsesProbeService_DoesNotCreateMainProcessSession()
    {
        PluginOverall.OnnxRuntimes[Source] = new()
        {
            ["test-isolated"] = () => throw new InvalidOperationException("A probe must not create a main-process session.")
        };
        var calls = new List<(string Device, bool ForceRefresh)>();
        var probe = new TestRuntimeProbe((device, _, forceRefresh) =>
        {
            calls.Add((device, forceRefresh));
            return Task.FromResult<bool?>(true);
        });
        var viewModel = new OnnxModelManagerPageViewModel(new TestConfigService(), new TestShell(), probe);
        await viewModel.RefreshCommand.ExecuteAsync(false);
        await viewModel.RefreshCommand.ExecuteAsync(true);
        CollectionAssert.AreEqual(new[] { ("test-isolated", false), ("test-isolated", true) }, calls);
        Assert.AreEqual(true, viewModel.Runtimes.Single(runtime => runtime.Device == "test-isolated").IsAvailable);
    }

    private sealed class TestRuntimeProbe(Func<string, CancellationToken, bool, Task<bool?>>? check = null) : IOnnxRuntimeProbe
    {
        public Task<bool?> CheckAsync(string device, CancellationToken cancellationToken = default, bool forceRefresh = false) =>
            check is not null ? check(device, cancellationToken, forceRefresh) : Task.Run(() =>
            {
                using var session = PluginOverall.GetOnnxRuntime(device)!();
                return session.CheckAvailability();
            }, cancellationToken);
    }

    private sealed class TestConfigService : IConfigService
    {
        public KitopiaConfig Config { get; } = new();
        public int SaveCount { get; private set; }
        public void Save(string key) => SaveCount++;
    }

    private sealed class TestShell : IDesktopShell
    {
        public string? LastOpened { get; private set; }
        public void Open(string path, string? arguments = "", string? workingDirectory = "") => LastOpened = path;
        public void RunAsAdmin(string path, string arguments = "") => throw new NotSupportedException();
        public void OpenFolderAndSelect(string path) => throw new NotSupportedException();
    }

    private class LegacySession : IInferenceSession
    {
        public string Device => "test";
        public IReadOnlyList<string> InputNames => [];
        public IReadOnlyList<int[]> OutputShape => [];
        public bool Disposed { get; private set; }
        public void InitSession(string modelPath) => throw new NotSupportedException();
        public void InitSession(byte[] modelData) => throw new NotSupportedException();
        public Memory<float> Infer(List<(string, Memory<int>, Memory<float>)> inputs) => throw new NotSupportedException();
        public void Dispose() => Disposed = true;
    }

    private sealed class ProbeSession(Func<bool?> check) : LegacySession, IInferenceSession
    {
        public bool? CheckAvailability() => check();
    }
}
