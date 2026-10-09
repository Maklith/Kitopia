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
        var viewModel = new OnnxModelManagerPageViewModel(config, shell);

        await viewModel.RefreshCommand.ExecuteAsync(null);
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
        await viewModel.RefreshCommand.ExecuteAsync(null);
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
        var viewModel = new OnnxModelManagerPageViewModel(new TestConfigService(), new TestShell());
        var task = viewModel.RefreshCommand.ExecuteAsync(null);
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
    [DataRow("zh-CN", 900, false)]
    [DataRow("en-US", 620, false)]
    [DataRow("zh-CN", 620, true)]
    public async Task ModelManager_DiagnosticsAndBackendSelection_RenderAndUpdate(string language, int width, bool dark)
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
            var viewModel = new OnnxModelManagerPageViewModel(config, new TestShell());
            var page = new OnnxModelManagerPage { DataContext = viewModel };
            var window = new Window { Content = page, Width = width, Height = 900,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
            try
            {
                window.Show();
                await viewModel.RefreshCommand.ExecutionTask!;
                Dispatcher.UIThread.RunJobs();
                var row = viewModel.Models.Single(model => model.ModelInfo.Model.SignName == "test-ui");
                var grid = page.FindControl<DataGrid>("ModelsGrid")!;
                grid.ScrollIntoView(row, grid.Columns[3]);
                var scroll = page.FindControl<ScrollViewer>("PageScroll")!;
                scroll.Offset = new Vector(0, scroll.Extent.Height);
                Dispatcher.UIThread.RunJobs();
                var radios = page.GetVisualDescendants().OfType<RadioButton>()
                    .Where(button => ReferenceEquals(button.Command, row.SelectRuntimeCommand)).ToArray();
                Assert.HasCount(5, radios);
                var cuda = radios.Single(button => (string)button.Content! == "GPU(CUDA)");
                Assert.IsFalse(cuda.IsEnabled);
                Assert.IsTrue(cuda.IsChecked);
                var cpu = radios.Single(button => (string)button.Content! == "CPU");
                Assert.IsTrue(cpu.IsEnabled);
                cpu.Focus();
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual("CPU", row.CurrentDevice);
                Assert.IsTrue(cpu.IsChecked);
                Assert.IsFalse(cuda.IsChecked);
                Assert.AreEqual(1, config.SaveCount);

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
                Assert.Contains(Lang.Get("lang.kitopia.onnx.cuda_requirements"), texts);
                var cpuRequirements = page.GetVisualDescendants().OfType<TextBlock>()
                    .Single(text => text.Text == Lang.Get("lang.kitopia.onnx.cpu_requirements"));
                Assert.IsFalse(cpuRequirements.IsVisible);
                var cpuGuide = page.GetVisualDescendants().OfType<Button>().Single(button =>
                    button.Command == viewModel.OpenDocumentationCommand &&
                    button.CommandParameter is OnnxRuntimeStatus { Device: "CPU" });
                Assert.IsFalse(cpuGuide.IsEffectivelyVisible);
                foreach (var expander in page.GetVisualDescendants().OfType<Expander>().Where(expander => expander.IsVisible))
                    expander.IsExpanded = true;
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
