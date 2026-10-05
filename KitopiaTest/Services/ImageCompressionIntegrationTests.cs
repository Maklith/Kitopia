using System.Reflection;
using Avalonia.Headless;
using Avalonia.Threading;
using Kitopia.Desktop.Features.Services.MQTT;
using KitopiaEx.ImageCompression;
using KitopiaEx.SearchWindow.InputDataAnalyzer;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using PluginCore;
using PluginCore.CustomScenario;
using PluginCore.Media;
using PluginCore.SearchWindow.InputData;
using PluginCore.SearchWindow.InputDataAnalyzer;
using SkiaSharp;
using HostKitopia = PluginCore.Kitopia;

namespace KitopiaTest.Services;

[TestClass]
[DoNotParallelize]
public sealed class ImageCompressionIntegrationTests
{
    private string _directory = null!;
    private ServiceProvider _services = null!;
    private IServiceProvider _originalHost = null!;
    private IServiceProvider _originalServices = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "Kitopia image entry tests " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _originalHost = HostKitopia.ServiceProvider;
        _originalServices = ServiceManager.Services;
        _services = new ServiceCollection().AddSingleton<Ffmpeg>()
            .AddSingleton<IExplorerContextMenuConfiger>(new MemoryContextMenu()).BuildServiceProvider();
        HostKitopia.ServiceProvider = _services;
        ServiceManager.Services = _services;
    }

    [TestCleanup]
    public void Cleanup()
    {
        HostKitopia.ServiceProvider = _originalHost;
        ServiceManager.Services = _originalServices;
        _services.Dispose();
        Directory.Delete(_directory, true);
    }

    [TestMethod]
    public async Task AnalyzeInputData_ImagePaths_ImportsOneDistinctBatchAndReusesWindow()
    {
        var first = CreateImage("first photo.png");
        var second = CreateImage("second photo.JPG");
        var text = Path.Combine(_directory, "document.txt");
        File.WriteAllText(text, "text");
        var analyzer = new ImageAnalyzer();
        Assert.IsTrue(analyzer.AnalyzeTimeFlags.HasFlag(InputDataAnalyzeTimeFlags.InputChanged));
        Assert.AreEqual(0, analyzer.AnalyzeInputData([
            new InputData { InputType = InputType.文件, Data = text },
            new InputData { InputType = InputType.文件, Data = Path.Combine(_directory, "missing.png") }
        ]).Count());
        var actions = analyzer.AnalyzeInputData(new[] { first, second, first, text }.Select(path =>
            new InputData { InputType = InputType.文件, Data = path })).ToArray();
        Assert.AreEqual(1, actions.Length);
        Assert.IsTrue(actions[0].ShowAsMiniApp);

        await using var session = HeadlessUnitTestSession.StartNew(typeof(ImageCompressionWindowTests));
        await session.Dispatch(async () =>
        {
            actions[0].Action!(null, null);
            await ImageCompressionWindow.OpenFilesAsync([]);
            var window = (ImageCompressionWindow)typeof(ImageCompressionWindow)
                .GetProperty("Current", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            try
            {
                var viewModel = (ImageCompressionViewModel)window.DataContext!;
                CollectionAssert.AreEquivalent(new[] { first, second }, viewModel.Items.Select(item => item.SourcePath).ToArray());
                var third = CreateImage("third.png");
                await ImageCompressionWindow.OpenFilesAsync([third, first]);
                Assert.AreSame(window, typeof(ImageCompressionWindow)
                    .GetProperty("Current", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null));
                Assert.AreEqual(3, viewModel.Items.Count);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task AnalyzeInputData_ClipboardImage_SnapshotsPixelsAndCleansSourceWithoutDeletingOutput()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ImageCompressionWindowTests));
        await session.Dispatch(async () =>
        {
            using var image = new Mat(48, 64, MatType.CV_8UC4, new Scalar(30, 80, 180, 255));
            var actions = new ImageAnalyzer().AnalyzeInputData([
                new InputData { InputType = InputType.图像, Data = "invalid" },
                new InputData { InputType = InputType.图像, Data = image }
            ]).ToArray();
            Assert.AreEqual(3, actions.Length);
            actions[0].Action!(null, null);
            image.Dispose();
            await ImageCompressionWindow.OpenFilesAsync([]);
            var window = (ImageCompressionWindow)typeof(ImageCompressionWindow)
                .GetProperty("Current", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            string source;
            string output;
            try
            {
                var viewModel = (ImageCompressionViewModel)window.DataContext!;
                var item = viewModel.Items.Single();
                Assert.IsTrue(item.IsTemporary);
                source = item.SourcePath;
                using var snapshot = Cv2.ImRead(source, ImreadModes.Unchanged);
                Assert.AreEqual(new Vec4b(30, 80, 180, 255), snapshot.At<Vec4b>(0, 0));
                Assert.IsTrue(viewModel.FfmpegAvailable);
                viewModel.OutputDirectory = Path.Combine(_directory, "output");
                viewModel.SkipLarger = false;
                await viewModel.StartCommand.ExecuteAsync(null);
                Assert.AreEqual(ImageCompressionStatus.Completed, item.Status, item.Error);
                output = item.OutputPath!;
                viewModel.RemoveCommand.Execute(item);
                Assert.IsFalse(File.Exists(source));
                Cv2.ImEncode(".png", snapshot, out var bytes);
                await viewModel.AddPathsAsync([], bytes);
                source = viewModel.Items.Single().SourcePath;
            }
            finally { window.Close(); }
            Assert.IsFalse(File.Exists(source));
            Assert.IsTrue(File.Exists(output));
            return true;
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task AddPathsAsync_OverlappingImportsAndRunningCompression_DoesNotLoseInputs()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ImageCompressionWindowTests));
        await session.Dispatch(async () =>
        {
            using var viewModel = new ImageCompressionViewModel(new Ffmpeg());
            var first = CreateImage("first.png");
            var second = CreateImage("second.png");
            await Task.WhenAll(viewModel.AddPathsAsync([first]), viewModel.AddPathsAsync([second, first]));
            Assert.AreEqual(2, viewModel.Items.Count);
            Assert.IsFalse(viewModel.IsAdding);
            viewModel.SkipLarger = false;
            var compression = viewModel.StartCommand.ExecuteAsync(null);
            var third = CreateImage("third.png");
            await Task.WhenAll(compression, viewModel.AddPathsAsync([third]));
            Assert.AreEqual(3, viewModel.Items.Count);
            Assert.IsTrue(viewModel.Items.Take(2).All(item => item.Status == ImageCompressionStatus.Completed));
            Assert.AreEqual(ImageCompressionStatus.Waiting, viewModel.Items[2].Status);
            viewModel.Dispose();
            await viewModel.AddPathsAsync([CreateImage("after close.png")]);
            Assert.AreEqual(3, viewModel.Items.Count);
            return true;
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task PluginLifecycle_ContextMenuAndStartupHandler_RegistersRoutesBatchAndRemovesEntries()
    {
        var originalTypes = HostKitopia.TypeNames;
        var originalTooltips = HostKitopia.ToolTipConverters;
        var originalConverters = HostKitopia.JsonConverters;
        var originalPluginServices = global::KitopiaEx.KitopiaEx.ServiceProvider;
        StartupArgumentManager.Handlers.TryGetValue(StartupAction.ImageCompression, out var originalHandler);
        HostKitopia.TypeNames = new Dictionary<string, string>();
        HostKitopia.ToolTipConverters = new Dictionary<Type, Func<object, string>>();
        HostKitopia.JsonConverters = new Dictionary<Type, ICustomScenarioValueSerializer>();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ImageCompressionWindowTests));
        await session.Dispatch(async () =>
        {
            var plugin = new global::KitopiaEx.KitopiaEx();
            var menu = _services.GetRequiredService<IExplorerContextMenuConfiger>();
            var unrelated = new ContextMenuItem { Title = "Other action" };
            menu.AddMenuItem(unrelated);
            menu.AddMenuItem(new ContextMenuItem
            {
                Title = "Old language title",
                Command = Path.Combine(AppContext.BaseDirectory, "Kitopia.Desktop.exe"),
                Arguments = StartupArgumentManager.GenerateCmd(StartupAction.ImageCompression, "{all}")
            });
            try
            {
                plugin.OnEnabled(_services, new Dictionary<string, IServiceProvider>());
                var entry = menu.GetAllMenuItems().Single(item => item != unrelated);
                StringAssert.Contains(entry.Arguments, "{all}");
                Assert.IsTrue(StartupArgumentManager.Handlers.ContainsKey(StartupAction.ImageCompression));
                var paths = new[] { CreateImage("first photo.png"), CreateImage("第二张图片.png") };
                var routing = MqttManager.ProcessLocalArgs(["-action:ImageCompression", "-value:" + paths[0], paths[1]]);
                if (!MqttManager.PluginsReady.Task.IsCompleted)
                {
                    Assert.IsFalse(routing.IsCompleted);
                    MqttManager.PluginsReady.TrySetResult();
                }
                await routing;
                var window = (ImageCompressionWindow)typeof(ImageCompressionWindow)
                    .GetProperty("Current", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
                CollectionAssert.AreEquivalent(paths, ((ImageCompressionViewModel)window.DataContext!).Items
                    .Select(item => item.SourcePath).ToArray());
            }
            finally
            {
                plugin.OnDisabled();
                Dispatcher.UIThread.RunJobs();
                Assert.IsFalse(StartupArgumentManager.Handlers.ContainsKey(StartupAction.ImageCompression));
                CollectionAssert.AreEqual(new[] { unrelated }, menu.GetAllMenuItems());
                HostKitopia.TypeNames = originalTypes;
                HostKitopia.ToolTipConverters = originalTooltips;
                HostKitopia.JsonConverters = originalConverters;
                global::KitopiaEx.KitopiaEx.ServiceProvider = originalPluginServices;
                if (originalHandler != null) StartupArgumentManager.Handlers[StartupAction.ImageCompression] = originalHandler;
            }
            return true;
        }, CancellationToken.None);
    }

    private string CreateImage(string name)
    {
        var path = Path.Combine(_directory, name);
        using var bitmap = new SKBitmap(64, 48);
        bitmap.Erase(new SKColor(180, 80, 30));
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(path);
        encoded.SaveTo(file);
        return path;
    }

    private sealed class MemoryContextMenu : IExplorerContextMenuConfiger
    {
        private readonly List<ContextMenuItem> _items = [];
        public bool OverwriteMenuItems(List<ContextMenuItem> items) { _items.Clear(); _items.AddRange(items); return true; }
        public bool AddMenuItem(ContextMenuItem item) { _items.Add(item); return true; }
        public bool RemoveMenuItem(string title) => _items.RemoveAll(item => item.Title == title) > 0;
        public bool RemoveMenuItem(ContextMenuItem item) => _items.Remove(item);
        public List<ContextMenuItem> GetAllMenuItems() => [.. _items];
    }
}
