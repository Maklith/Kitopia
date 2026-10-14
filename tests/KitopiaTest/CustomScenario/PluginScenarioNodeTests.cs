using Kitopia.Desktop.Abstractions.Shell;
using System.Reflection;
using System.Text.Json.Serialization;
using Kitopia.Desktop.Features.CustomScenario;
using Kitopia.Desktop.Features.Services.Plugin;
using Kitopia.Desktop.Features.Utils;
using Kitopia.Desktop.Platform.Windows;
using KitopiaEx.CustomScenarioMethods;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using PluginCore;
using PluginCore.CustomScenario;
using PluginCore.CustomScenario.Attribute.Scenario;
using ImageNode = KitopiaEx.CustomScenarioMethods.Image;
using PluginHost = PluginCore.Kitopia;

namespace KitopiaTest.CustomScenario;

[TestClass]
[DoNotParallelize]
public sealed class PluginScenarioNodeTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void SaveImage_SelectedBehavior_UsesCorrectServiceAndReportsFailure(bool openFolder, bool succeeds)
    {
        var previousServices = PluginHost.ServiceProvider;
        var imageTool = new RecordingImageTool { Succeeds = succeeds };
        using var services = new ServiceCollection().AddSingleton<IImageTool>(imageTool).BuildServiceProvider();
        using var image = new Mat(1, 1, MatType.CV_8UC4, Scalar.All(255));
        try
        {
            PluginHost.ServiceProvider = services;
            var node = new ImageNode();
            void Save()
            {
                var capture = new ScreenCaptureResult { Source = image };
                var saved = openFolder
                    ? node.SaveImageToPathAndOpenFolder(capture, "image.png", CancellationToken.None)
                    : node.SaveImageToPath(capture, "image.png", CancellationToken.None);
                Assert.AreEqual(Path.GetFullPath("image.png"), saved);
            }

            if (succeeds) Save();
            else Assert.ThrowsExactly<IOException>(Save);
            Assert.AreEqual(openFolder ? 0 : 1, imageTool.SaveCalls);
            Assert.AreEqual(openFolder ? 1 : 0, imageTool.SaveAndOpenCalls);
            Assert.AreEqual(Path.GetFullPath("image.png"), imageTool.Path);
        }
        finally
        {
            PluginHost.ServiceProvider = previousServices;
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SaveImageAndOpenFolder_ReturnsSaveResultAndOnlyOpensOnSuccess(bool succeeds)
    {
        var previousServices = ServiceManager.Services;
        var shell = new RecordingShell();
        using var services = new ServiceCollection().AddSingleton<IDesktopShell>(shell).BuildServiceProvider();
        var directory = Path.Combine(Path.GetTempPath(), "KitopiaImageTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, succeeds ? "image.png" : Path.Combine("missing", "image.png"));
        using var image = new Mat(1, 1, MatType.CV_8UC4, Scalar.All(255));
        try
        {
            ServiceManager.Services = services;

            Assert.AreEqual(succeeds, new ImageTool().SaveImageAndOpenTheFolder(image, path));
            Assert.AreEqual(succeeds ? path : null, shell.SelectedPath);
            Assert.AreEqual(succeeds, File.Exists(path));
        }
        finally
        {
            ServiceManager.Services = previousServices;
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CopyImageToClipboard_AwaitsCompletionAndReportsFailure(bool succeeds)
    {
        var previousServices = PluginHost.ServiceProvider;
        var clipboard = new PendingClipboard();
        using var services = new ServiceCollection().AddSingleton<IClipboardService>(clipboard).BuildServiceProvider();
        using var image = new Mat(1, 1, MatType.CV_8UC4, Scalar.All(255));
        try
        {
            PluginHost.ServiceProvider = services;
            var copy = new ImageNode().CopyImageToClipboard(new ScreenCaptureResult { Source = image }, CancellationToken.None);
            Assert.IsFalse(copy.IsCompleted);

            clipboard.Completion.SetResult(succeeds);

            if (succeeds) await copy.WaitAsync(TimeSpan.FromSeconds(5));
            else await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => copy);
        }
        finally
        {
            PluginHost.ServiceProvider = previousServices;
        }
    }

    [TestMethod]
    public async Task InteractiveCapture_CancelledScenario_StopsWaitingAndDisposesLateResult()
    {
        var previousServices = ServiceManager.Services;
        var picker = new CallbackCapturePicker();
        using var services = new ServiceCollection().AddSingleton<IScreenCaptureWindow>(picker).BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        try
        {
            ServiceManager.Services = services;
            var capture = new ScreenCaptureNode().ScreenshotTheSelectArea(cancellation.Token);
            Assert.IsFalse(capture.IsCompleted);
            Assert.IsNotNull(picker.Selected);

            cancellation.Cancel();

            await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => capture.WaitAsync(TimeSpan.FromSeconds(5)));
            using var lateImage = new Mat(1, 1, MatType.CV_8UC4);
            picker.Selected(new ScreenCaptureResult { Source = lateImage });
            Assert.IsTrue(lateImage.IsDisposed);
        }
        finally
        {
            ServiceManager.Services = previousServices;
        }
    }

    [TestMethod]
    public async Task InteractiveCapture_PluginInvocation_AwaitsImageAndPreservesOutputType()
    {
        var previousServices = ServiceManager.Services;
        var picker = new CallbackCapturePicker();
        using var services = new ServiceCollection().AddSingleton<IScreenCaptureWindow>(picker)
            .AddSingleton<ScreenCaptureNode>().BuildServiceProvider();
        using var image = new Mat(1, 1, MatType.CV_8UC4);
        try
        {
            ServiceManager.Services = services;
            var method = typeof(ScreenCaptureNode).GetMethod(nameof(ScreenCaptureNode.ScreenshotTheSelectArea),
                [typeof(CancellationToken)])!;
            var node = new ScenarioMethod(method, new PluginLocalInfo(), new ScenarioMethodAttribute("capture"),
                ScenarioMethodType.PluginMethod, services).GenerateNode();
            var values = new ObservableDictionary<string, CustomScenarioValue>();
            var invocation = node.InvokeAsync(CancellationToken.None, [], values, values, values).AsTask();
            Assert.AreEqual(typeof(ScreenCaptureResult), node.Output[1].InputObject.SerializeType);
            Assert.IsFalse(invocation.IsCompleted);

            picker.Selected!(new ScreenCaptureResult { Source = image });

            Assert.IsTrue(await invocation.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.AreSame(image, ((ScreenCaptureResult)node.Output[1].InputObject.Value!).Source);
        }
        finally
        {
            ServiceManager.Services = previousServices;
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SaveImage_LegacyConnectors_UpgradeWithoutReplacingConnectionsAndProducePath(bool defaultSave)
    {
        var previousServices = PluginHost.ServiceProvider;
        var imageTool = new RecordingImageTool { Succeeds = true };
        using var services = new ServiceCollection().AddSingleton<IImageTool>(imageTool)
            .AddSingleton<ScreenCaptureNode>().AddSingleton<ImageNode>().BuildServiceProvider();
        using var image = new Mat(1, 1, MatType.CV_8UC4);
        try
        {
            PluginHost.ServiceProvider = services;
            var method = defaultSave
                ? typeof(ScreenCaptureNode).GetMethod(nameof(ScreenCaptureNode.SaveImage))!
                : typeof(ImageNode).GetMethod(nameof(ImageNode.SaveImageToPath))!;
            var attribute = method.GetCustomAttribute<ScenarioMethodAttribute>()!;
            var node = new ScenarioMethod(method, new PluginLocalInfo(), attribute, ScenarioMethodType.PluginMethod,
                services).GenerateNode();
            var captureInput = node.Input[1];
            var flowOutput = node.Output[0];
            node.Title = "My save node";
            node.Output.RemoveAt(1);
            if (defaultSave) node.Input.RemoveAt(2);
            else node.Input[2].InputObject.Value = "custom.png";

            ((IJsonOnDeserialized)node).OnDeserialized();
            ((IJsonOnDeserialized)node).OnDeserialized();

            Assert.HasCount(3, node.Input);
            Assert.HasCount(2, node.Output);
            Assert.AreSame(captureInput, node.Input[1]);
            Assert.AreSame(flowOutput, node.Output[0]);
            Assert.AreEqual("My save node", node.Title);
            Assert.AreSame(node, node.Output[1].Source);
            Assert.AreEqual(typeof(string), node.Output[1].InputObject.SerializeType);
            Assert.IsTrue(node.Input[2].InputObject.IsSelf);
            if (defaultSave) Assert.IsNull(node.Input[2].InputObject.Value);
            captureInput.InputObject.Value = new ScreenCaptureResult { Source = image };
            var values = new ObservableDictionary<string, CustomScenarioValue>();

            Assert.IsTrue(await node.InvokeAsync(CancellationToken.None, [], values, values, values));

            Assert.AreEqual(imageTool.Path, node.Output[1].InputObject.Value);
            if (defaultSave)
                Assert.AreEqual(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                    Path.GetDirectoryName(imageTool.Path));
            else Assert.AreEqual(Path.GetFullPath("custom.png"), imageTool.Path);
        }
        finally
        {
            PluginHost.ServiceProvider = previousServices;
        }
    }

    [TestMethod]
    public void DefaultSave_CustomPath_UsesImageServiceAndReturnsAbsolutePath()
    {
        var previousServices = PluginHost.ServiceProvider;
        var imageTool = new RecordingImageTool { Succeeds = true };
        using var services = new ServiceCollection().AddSingleton<IImageTool>(imageTool).BuildServiceProvider();
        using var image = new Mat(1, 1, MatType.CV_8UC4);
        try
        {
            PluginHost.ServiceProvider = services;
            var path = new ScreenCaptureNode().SaveImage(new ScreenCaptureResult { Source = image }, "custom.png");
            Assert.AreEqual(Path.GetFullPath("custom.png"), path);
            Assert.AreEqual(path, imageTool.Path);
            Assert.AreEqual(1, imageTool.SaveCalls);
            Assert.AreEqual(0, imageTool.SaveAndOpenCalls);
        }
        finally
        {
            PluginHost.ServiceProvider = previousServices;
        }
    }

    [TestMethod]
    public async Task LocalItem_PluginInvocation_PassesTypedScenarioValues()
    {
        var previousSearch = PluginHost.ISearchItemTool;
        var search = new RecordingSearchItemTool();
        using var services = new ServiceCollection().AddSingleton<SearchItemScenarioMethod>().BuildServiceProvider();
        try
        {
            PluginHost.ISearchItemTool = search;
            var method = typeof(SearchItemScenarioMethod).GetMethod(nameof(SearchItemScenarioMethod.OpenSearchViewItem))!;
            var node = new ScenarioMethod(method, new PluginLocalInfo(), method.GetCustomAttribute<ScenarioMethodAttribute>()!,
                ScenarioMethodType.PluginMethod, services).GenerateNode();
            Assert.HasCount(2, node.Input);
            Assert.AreEqual(typeof(SearchViewItem), node.Input[1].InputObject.ShowType);
            Assert.IsTrue(node.Input[1].InputObject.IsSelf);
            node.Input[1].InputObject.Value = "CustomScenario:child";
            node.Input.Add(new ConnectorItem { Source = node, InputObject = new CustomScenarioValue(typeof(int), 7) });
            node.Input.Add(new ConnectorItem { Source = node, InputObject = new CustomScenarioValue(typeof(string), "text") });
            var values = new ObservableDictionary<string, CustomScenarioValue>();

            Assert.IsTrue(await node.InvokeAsync(CancellationToken.None, [], values, values, values));

            Assert.AreEqual("CustomScenario:child", search.OnlyKey);
            CollectionAssert.AreEqual(new object[] { 7, "text" }, search.Inputs);
        }
        finally
        {
            PluginHost.ISearchItemTool = previousSearch;
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void OcrText_ComposesWithClipboardAndReportsFailures(bool succeeds)
    {
        var previousServices = PluginHost.ServiceProvider;
        var clipboard = new PendingClipboard { TextSucceeds = succeeds };
        using var services = new ServiceCollection().AddSingleton<IClipboardService>(clipboard).BuildServiceProvider();
        try
        {
            PluginHost.ServiceProvider = services;
            var ocr = new KitopiaEx.CustomScenarioMethods.Ocr();
            var results = new[] { new KitopiaEx.Ocr.OcrResult { Text = "first" }, new KitopiaEx.Ocr.OcrResult { Text = "second" } };
            var text = ocr.JoinOcrText(results);
            Assert.AreEqual("first\nsecond", text);
            Assert.AreEqual("first second", ocr.JoinOcrText(results, " "));
            Assert.AreEqual(string.Empty, ocr.JoinOcrText([]));
            if (succeeds) ocr.CopyTextToClipboard(text, CancellationToken.None);
            else Assert.ThrowsExactly<InvalidOperationException>(() => ocr.CopyTextToClipboard(text, CancellationToken.None));
            Assert.AreEqual(text, clipboard.Text);
        }
        finally
        {
            PluginHost.ServiceProvider = previousServices;
        }
    }

    private sealed class RecordingSearchItemTool : ISearchItemTool
    {
        public string? OnlyKey { get; private set; }
        public object[] Inputs { get; private set; } = [];
        public void OpenSearchItemByOnlyKey(string onlyKey, params object[] inputValues)
        {
            OnlyKey = onlyKey;
            Inputs = inputValues;
        }
        public void OpenFile(SearchViewItem? item, params object[] inputValues) => throw new NotSupportedException();
        public void IgnoreItem(SearchViewItem? item) => throw new NotSupportedException();
        public void OpenFolder(SearchViewItem? item) => throw new NotSupportedException();
        public void RunAsAdmin(SearchViewItem? item) => throw new NotSupportedException();
        public void Star(SearchViewItem item) => throw new NotSupportedException();
        public void Pin(SearchViewItem? item) => throw new NotSupportedException();
        public void OpenFolderInTerminal(SearchViewItem? item) => throw new NotSupportedException();
    }

    private sealed class RecordingImageTool : IImageTool
    {
        public bool Succeeds { get; init; }
        public int SaveCalls { get; private set; }
        public int SaveAndOpenCalls { get; private set; }
        public string? Path { get; private set; }
        public bool SaveImage(Mat image, string filePath = null!) { Path = filePath; SaveCalls++; return Succeeds; }
        public bool SaveImageAndOpenTheFolder(Mat image, string filePath = null!) { Path = filePath; SaveAndOpenCalls++; return Succeeds; }
    }

    private sealed class RecordingShell : IDesktopShell
    {
        public string? SelectedPath { get; private set; }
        public void Open(string path, string? arguments = "", string? workingDirectory = "") => throw new NotSupportedException();
        public void RunAsAdmin(string path, string arguments = "") => throw new NotSupportedException();
        public void OpenFolderAndSelect(string path) => SelectedPath = path;
    }

    private sealed class PendingClipboard : IClipboardService
    {
        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<bool> SetImageAsync(ScreenCaptureResult result) => Completion.Task;
        public bool TextSucceeds { get; init; }
        public string? Text { get; private set; }
        public bool HasText() => throw new NotSupportedException();
        public string? GetText() => throw new NotSupportedException();
        public bool SetText(string text) { Text = text; return TextSucceeds; }
        public bool HasFiles() => throw new NotSupportedException();
        public IReadOnlyList<string> GetFiles() => throw new NotSupportedException();
        public bool HasImage() => throw new NotSupportedException();
        public Mat? GetImage() => throw new NotSupportedException();
    }

    private sealed class CallbackCapturePicker : IScreenCaptureWindow
    {
        public Action<ScreenCaptureResult>? Selected { get; private set; }
        public void RequestUserSelectScreenBytes(Action<ScreenCaptureResult> action, Action cancel) => Selected = action;
        public void CaptureScreen() => throw new NotSupportedException();
        public void RequestUserSelectScreenInfo(Action<ScreenCaptureInfo> action) => throw new NotSupportedException();
        public Task<ScreenCaptureInfo> GetScreenCaptureInfo() => throw new NotSupportedException();
    }
}
