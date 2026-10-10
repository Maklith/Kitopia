using System.ComponentModel;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Kitopia.Desktop.Controls;
using Kitopia.Desktop.Features.Indexing;
using Kitopia.Desktop.Features.Search;
using Kitopia.Desktop.Features.Search.ViewModels;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Plugin;
using Kitopia.Desktop.Platform.Windows;
using Kitopia.Desktop.Windows;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Config;
using SkiaSharp;

namespace KitopiaTest;

[TestClass]
[DoNotParallelize]
public sealed class FilePreviewInteractionTests
{
    private string _directory = null!;
    private string _document = null!;
    private string _image = null!;
    private string _text = null!;

    public TestContext TestContext { get; set; } = null!;

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Kitopia.Desktop.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia();

    [TestInitialize]
    public void Initialize()
    {
        _directory = Directory.CreateTempSubdirectory("Kitopia-preview-interaction-").FullName;
        _document = Path.Combine(_directory, "document.docx");
        _image = Path.Combine(_directory, "image.png");
        _text = Path.Combine(_directory, "text.txt");
        File.WriteAllBytes(_document, [1, 2, 3]);
        File.WriteAllText(_text, "Latest text preview");
        using var bitmap = new SKBitmap(160, 126);
        bitmap.Erase(new SKColor(34, 139, 88));
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(_image, encoded.ToArray());
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    [TestMethod]
    public async Task SetFilesAsync_BackgroundFormatChanges_ReplacesNativePreviewWithLatestContent()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(FilePreviewInteractionTests));
        await session.Dispatch(async () =>
        {
            using var model = new MouseQuickWindowViewModel();
            var control = new FilePreviewControl { DataContext = model };
            var window = new Window { Content = control, Width = 640, Height = 480 };
            try
            {
                window.Show();
                await model.SetFilesAsync([_document]);
                Dispatcher.UIThread.RunJobs();
                var native = control.FindControl<ContentControl>("NativePreview")!;
                Assert.AreEqual(_document, ((NativeFilePreviewHost)native.Content!).FilePath);

                await Task.Run(() => model.SetFilesAsync([_image]));
                Dispatcher.UIThread.RunJobs();
                Assert.IsNull(native.Content);
                Assert.AreEqual(_image, model.SelectedPath);
                Assert.AreEqual(Path.GetFileName(_image), model.FileName);
                Assert.IsNotNull(model.PreviewImage);
                Assert.AreSame(model.PreviewImage, control.FindControl<Image>("ImagePreview")!.Source);
                Assert.IsFalse(model.IsLoading);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = window.CaptureRenderedFrame();
                Assert.IsNotNull(frame);
                var screenshot = Path.Combine(TestContext.TestRunDirectory!, "preview-document-to-image.png");
                frame.Save(screenshot);
                TestContext.AddResultFile(screenshot);

                await Task.Run(() => model.SetFilesAsync([_document]));
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(_document, ((NativeFilePreviewHost)native.Content!).FilePath);
                await Task.Run(() => model.SetFilesAsync([_text]));
                Dispatcher.UIThread.RunJobs();
                Assert.IsNull(native.Content);
                Assert.IsNull(model.PreviewImage);
                Assert.AreEqual("Latest text preview", model.PreviewText);
                Assert.IsFalse(model.IsLoading);
            }
            finally
            {
                control.DataContext = null;
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task ToSearch_BackgroundInput_ClearsOldPreviewAndAllowsSelectionChangesOnUiThread()
    {
        ReactiveUI.Builder.RxAppBuilder.CreateReactiveUIBuilder().WithPlatformServices().BuildApp();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(FilePreviewInteractionTests));
        await session.Dispatch(async () =>
        {
            var originalConfigs = ConfigManger.Configs;
            var originalServices = ServiceManager.Services;
            var identifiers = PluginOverall.SearchWindowInputDataIdentifies.ToArray();
            var analyzers = PluginOverall.SearchWindowInputDataAnalyzers.ToArray();
            ConfigManger.Configs = new Dictionary<string, ConfigBase>
            {
                ["KitopiaConfig"] = new KitopiaConfig { useEverything = false, enableSemanticSearch = false }
            };
            PluginOverall.SearchWindowInputDataIdentifies.Clear();
            PluginOverall.SearchWindowInputDataAnalyzers.Clear();
            PluginOverall.SearchWindowInputDataIdentifies["Kitopia"] = [];
            PluginOverall.SearchWindowInputDataAnalyzers["Kitopia"] = [];
            using var index = new IndexService();
            typeof(IndexService).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(index, new IndexVectorStore(Path.Combine(_directory, "index.db")));
            using var services = new ServiceCollection().AddSingleton<IIndexService>(index).BuildServiceProvider();
            ServiceManager.Services = services;
            var model = new SearchWindowViewModel();
            var control = new FilePreviewControl { DataContext = model.FilePreview };
            var window = new Window { Content = control, Width = 640, Height = 480 };
            try
            {
                Dispatcher.UIThread.RunJobs();
                window.Show();
                var document = new SearchViewItem
                {
                    OnlyKey = _document, ItemDisplayName = Path.GetFileName(_document), FileType = (FileType)2
                };
                var image = new SearchViewItem
                {
                    OnlyKey = _image, ItemDisplayName = Path.GetFileName(_image), FileType = FileType.图像
                };
                model.IsPreviewMode = true;
                model.SelectedItem = document;
                await model.FilePreview.SetFilesAsync([_document]);
                Dispatcher.UIThread.RunJobs();
                var native = control.FindControl<ContentControl>("NativePreview")!;
                Assert.IsNotNull(native.Content);
                var updatesOnUiThread = true;
                model.PropertyChanged += (_, _) => updatesOnUiThread &= Dispatcher.UIThread.CheckAccess();

                await Task.Run(() => model.ToSearch("d"));
                Dispatcher.UIThread.RunJobs();
                Assert.IsTrue(updatesOnUiThread);
                Assert.IsFalse(model.IsPreviewMode);
                Assert.IsNull(model.SelectedItem);
                Assert.AreEqual("", model.FilePreview.SelectedPath);
                Assert.IsNull(native.Content);

                model.ToSearch(null);
                Dispatcher.UIThread.RunJobs();
                model.IsPreviewMode = true;
                foreach (var item in new[] { image, document, image, document, image })
                {
                    var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    PropertyChangedEventHandler changed = (_, _) =>
                    {
                        if (model.FilePreview.SelectedPath == item.OnlyKey
                            && (model.FilePreview.NativePath == item.OnlyKey || model.FilePreview.PreviewImage is not null))
                            loaded.TrySetResult();
                    };
                    model.FilePreview.PropertyChanged += changed;
                    try
                    {
                        model.ClickItem(item);
                        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
                        Dispatcher.UIThread.RunJobs();
                        Assert.AreSame(item, model.SelectedItem);
                        Assert.AreEqual(item.OnlyKey, model.FilePreview.SelectedPath);
                        if (ReferenceEquals(item, image))
                        {
                            Assert.IsNull(native.Content);
                            Assert.AreSame(model.FilePreview.PreviewImage, control.FindControl<Image>("ImagePreview")!.Source);
                        }
                        else Assert.AreEqual(_document, ((NativeFilePreviewHost)native.Content!).FilePath);
                    }
                    finally
                    {
                        model.FilePreview.PropertyChanged -= changed;
                    }
                }
            }
            finally
            {
                model.ToSearch(null);
                Dispatcher.UIThread.RunJobs();
                control.DataContext = null;
                model.FilePreview.Dispose();
                window.Close();
                ServiceManager.Services = originalServices;
                ConfigManger.Configs = originalConfigs;
                PluginOverall.SearchWindowInputDataIdentifies.Clear();
                PluginOverall.SearchWindowInputDataAnalyzers.Clear();
                foreach (var pair in identifiers) PluginOverall.SearchWindowInputDataIdentifies[pair.Key] = pair.Value;
                foreach (var pair in analyzers) PluginOverall.SearchWindowInputDataAnalyzers[pair.Key] = pair.Value;
            }
            return true;
        }, CancellationToken.None);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(20)]
    public async Task SearchWindow_PreviewMode_ContainsPortraitImageAndNativePreviewBetweenHeaderAndFooter(int resultCount)
    {
        ReactiveUI.Builder.RxAppBuilder.CreateReactiveUIBuilder().WithPlatformServices().BuildApp();
        var screenshots = new List<string>();
        await using (var session = HeadlessUnitTestSession.StartNew(typeof(FilePreviewInteractionTests)))
        {
            await session.Dispatch(async () =>
            {
                var originalConfigs = ConfigManger.Configs;
                var originalServices = ServiceManager.Services;
                var identifiers = PluginOverall.SearchWindowInputDataIdentifies.ToArray();
                var analyzers = PluginOverall.SearchWindowInputDataAnalyzers.ToArray();
                ConfigManger.Configs = new Dictionary<string, ConfigBase>
                {
                    ["KitopiaConfig"] = new KitopiaConfig { useEverything = false }
                };
                PluginOverall.SearchWindowInputDataIdentifies.Clear();
                PluginOverall.SearchWindowInputDataAnalyzers.Clear();
                PluginOverall.SearchWindowInputDataIdentifies["Kitopia"] = [];
                PluginOverall.SearchWindowInputDataAnalyzers["Kitopia"] = [];
                using var services = new ServiceCollection()
                    .AddSingleton(DispatchProxy.Create<IWindowTool, PreviewWindowServicesProxy>())
                    .AddSingleton(DispatchProxy.Create<IAppToolService, PreviewWindowServicesProxy>())
                    .BuildServiceProvider();
                ServiceManager.Services = services;
                var model = new SearchWindowViewModel();
                var window = new SearchWindow { DataContext = model };
                window.Closing -= typeof(SearchWindow).GetMethod("Window_OnClosing", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .CreateDelegate<EventHandler<WindowClosingEventArgs>>(window);
                try
                {
                    window.Show();
                    Dispatcher.UIThread.RunJobs();
                    Assert.IsTrue(window.Bounds.Height < window.MaxHeight);
                    using (var bitmap = new SKBitmap(720, 1280))
                    {
                        bitmap.Erase(new SKColor(34, 139, 88));
                        using var image = SKImage.FromBitmap(bitmap);
                        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                        File.WriteAllBytes(_image, encoded.ToArray());
                    }
                    model.Items.AddRange(Enumerable.Range(0, resultCount).Select(index => new SearchViewItem
                    {
                        OnlyKey = index == 0 ? _image : _text + index,
                        ItemDisplayName = index == 0 ? "Portrait image" : $"Search result {index}",
                        FileType = FileType.图像
                    }));
                    model.FileTypes.Add(new FileTypeFilter { FileType = FileType.图像, IsChecked = true });
                    model.ShowFileTypeFilter = true;
                    model.IsPreviewMode = true;
                    model.SelectedItem = model.Items[0];
                    await model.FilePreview.SetFilesAsync([_image]);
                    Dispatcher.UIThread.RunJobs();

                    Assert.AreEqual(960, window.Bounds.Width);
                    Assert.AreEqual(768, window.Bounds.Height);
                    var preview = window.FindControl<FilePreviewControl>("FilePreviewSurface")!;
                    var layout = (Grid)preview.Parent!.Parent!.Parent!;
                    var header = layout.Children.Single(child => Grid.GetRow(child) == 0);
                    var footer = layout.Children.Single(child => Grid.GetRow(child) == 2);
                    Assert.IsGreaterThan(0, preview.Bounds.Height);
                    Assert.IsTrue(preview.TranslatePoint(default, layout)!.Value.Y >= header.Bounds.Bottom,
                        "The preview must start below the search header.");
                    Assert.IsTrue(preview.TranslatePoint(new Point(0, preview.Bounds.Height), layout)!.Value.Y <= footer.Bounds.Top,
                        "The preview must end above the file details.");
                    var viewport = preview.FindControl<Grid>("ImageViewport")!;
                    var imagePreview = preview.FindControl<Image>("ImagePreview")!;
                    Assert.IsTrue(imagePreview.Bounds.Height <= preview.Bounds.Height);
                    Assert.AreEqual(viewport.Bounds.Height, imagePreview.Bounds.Height);

                    var zoom = preview.FindControl<Slider>("ImageZoom")!;
                    foreach (var scale in new[] { 1d, 4d })
                    {
                        zoom.Value = scale;
                        Dispatcher.UIThread.RunJobs();
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        using var frame = window.CaptureRenderedFrame();
                        Assert.IsNotNull(frame);
                        var screenshot = Path.Combine(TestContext.TestRunDirectory!, $"search-preview-{resultCount}-zoom-{scale}.png");
                        frame.Save(screenshot, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                        using var rendered = SKBitmap.Decode(screenshot);
                        var imageColor = new SKColor(34, 139, 88);
                        var imageCenter = viewport.TranslatePoint(viewport.Bounds.Center, window)!.Value * window.RenderScaling;
                        var headerBottom = header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height - 2), window)!.Value * window.RenderScaling;
                        var footerTop = footer.TranslatePoint(new Point(footer.Bounds.Width / 2, 2), window)!.Value * window.RenderScaling;
                        Assert.AreEqual(imageColor, rendered.GetPixel((int)imageCenter.X, (int)imageCenter.Y));
                        Assert.AreNotEqual(imageColor, rendered.GetPixel((int)headerBottom.X, (int)headerBottom.Y));
                        Assert.AreNotEqual(imageColor, rendered.GetPixel((int)footerTop.X, (int)footerTop.Y));
                        screenshots.Add(screenshot);
                    }

                    await model.FilePreview.SetFilesAsync([_document]);
                    Dispatcher.UIThread.RunJobs();
                    var native = (NativeFilePreviewHost)preview.FindControl<ContentControl>("NativePreview")!.Content!;
                    Assert.IsGreaterThan(0, native.Bounds.Height);
                    Assert.IsTrue(native.TranslatePoint(default, layout)!.Value.Y >= header.Bounds.Bottom);
                    Assert.IsTrue(native.TranslatePoint(new Point(0, native.Bounds.Height), layout)!.Value.Y <= footer.Bounds.Top);

                    model.IsPreviewMode = false;
                    model.Items.Clear();
                    model.SelectedItem = null;
                    Dispatcher.UIThread.RunJobs();
                    Assert.IsTrue(window.Bounds.Height < window.MaxHeight,
                        "The search window must shrink again after leaving preview mode.");
                }
                finally
                {
                    window.DataContext = null;
                    model.FilePreview.Dispose();
                    window.Close();
                    ServiceManager.Services = originalServices;
                    ConfigManger.Configs = originalConfigs;
                    PluginOverall.SearchWindowInputDataIdentifies.Clear();
                    PluginOverall.SearchWindowInputDataAnalyzers.Clear();
                    foreach (var pair in identifiers) PluginOverall.SearchWindowInputDataIdentifies[pair.Key] = pair.Value;
                    foreach (var pair in analyzers) PluginOverall.SearchWindowInputDataAnalyzers[pair.Key] = pair.Value;
                }
                return true;
            }, CancellationToken.None);
        }
        foreach (var screenshot in screenshots) TestContext.AddResultFile(screenshot);
    }

    private class PreviewWindowServicesProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod!.Name switch
        {
            nameof(IWindowTool.MoveWindowToMouseScreenCenter) or nameof(IWindowTool.SetForegroundWindow)
                or nameof(IAppToolService.LoadIcon) => null,
            _ => throw new NotSupportedException(targetMethod.Name)
        };
    }
}
