using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kitopia.Desktop.Features.Services.Plugin;
using KitopiaEx.ImageCompression;
using PluginCore;
using PluginCore.Media;
using SkiaSharp;
using HostLang = Kitopia.Feature.Localization.Lang;

namespace KitopiaTest.Services;

[TestClass]
[DoNotParallelize]
public sealed class ImageCompressionWindowTests
{
    public TestContext TestContext { get; set; } = null!;
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Kitopia.Desktop.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia();

    [TestMethod]
    [DataRow(1080, 740, "zh-CN", false)]
    [DataRow(900, 600, "en-US", false)]
    [DataRow(1080, 740, "zh-CN", true)]
    public async Task Window_ThemesAndCompactSize_RendersControlsAndRows(int width, int height, string language, bool dark)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ImageCompressionWindowTests));
        await session.Dispatch(() =>
        {
            var originalLanguage = HostLang.Current.Language;
            HostLang.Current.RegisterAssembly(typeof(ImageCompressionWindow).Assembly);
            HostLang.Current.UseLanguage(language);
            PluginCore.Localization.Lang.Lookup = HostLang.Get;
            PluginCore.Localization.Lang.BindingSource = HostLang.Current;
            Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var viewModel = new ImageCompressionViewModel(new Ffmpeg());
            using var pixelImage = new SKBitmap(96, 64);
            using (var canvas = new SKCanvas(pixelImage))
            {
                canvas.Clear(new SKColor(52, 117, 79));
                using var paint = new SKPaint { Color = new SKColor(214, 233, 235), IsAntialias = true };
                canvas.DrawRect(15, 15, 40, 34, paint);
            }
            using var previewImage = SKImage.FromBitmap(pixelImage);
            using var preview = previewImage.Encode(SKEncodedImageFormat.Png, 100);
            foreach (var status in Enum.GetValues<ImageCompressionStatus>())
            {
                using var stream = preview.AsStream();
                viewModel.Items.Add(new ImageCompressionItem(@"C:\Photos\product-photo-with-a-long-file-name-" + status + ".png",
                    1830000, new Bitmap(stream))
                {
                    Status = status,
                    OutputBytes = status is ImageCompressionStatus.Completed or ImageCompressionStatus.TargetUnmet ? 264000 : null,
                    Error = status == ImageCompressionStatus.Failed ? "Invalid image" : ""
                });
            }
            var window = new ImageCompressionWindow { DataContext = viewModel, Width = width, Height = height };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = window.CaptureRenderedFrame();
                Assert.IsNotNull(frame);
                var screenshot = Path.Combine(TestContext.TestRunDirectory!, $"image-compression-{language}-{width}-{dark}.png");
                frame.Save(screenshot);
                TestContext.AddResultFile(screenshot);
                var list = window.FindControl<ListBox>("FileList")!;
                Assert.IsTrue(list.Bounds.Width > 400);
                Assert.AreEqual(viewModel.Items.Count, list.Items.Count);
                var start = window.FindControl<Button>("StartButton")!;
                Assert.AreEqual(viewModel.FfmpegAvailable, start.IsEffectivelyEnabled);
                Assert.IsTrue(start.Bounds.Width >= 70 && start.Bounds.Height >= 34);
                var position = start.TranslatePoint(default, window)!.Value;
                Assert.IsTrue(position.X >= 0 && position.Y + start.Bounds.Height <= window.ClientSize.Height);
                foreach (var row in list.GetVisualDescendants().OfType<ListBoxItem>())
                {
                    Assert.IsTrue(row.Bounds.Width <= list.Bounds.Width);
                    var remove = row.GetVisualDescendants().OfType<Button>().Single();
                    Assert.IsTrue(remove.TranslatePoint(default, row)!.Value.X + remove.Bounds.Width <= row.Bounds.Width);
                }
                var radioButtons = window.GetVisualDescendants().OfType<RadioButton>().ToArray();
                radioButtons[1].IsChecked = true;
                Assert.AreEqual(ImageCompressionMode.TargetSize, viewModel.Mode);
                viewModel.Format = ImageCompressionFormat.PNG;
                Assert.AreEqual(ImageCompressionMode.Quality, viewModel.Mode);
                Assert.IsFalse(viewModel.IsLossy);
                viewModel.ClearCommand.Execute(null);
                Assert.IsTrue(viewModel.IsEmpty);
                Assert.IsFalse(start.IsEffectivelyEnabled);
            }
            finally
            {
                window.Close();
                HostLang.Current.UseLanguage(originalLanguage);
                HostLang.Current.UnregisterAssembly(typeof(ImageCompressionWindow).Assembly.GetName().Name!);
            }
            return true;
        }, CancellationToken.None);
    }

    [TestMethod]
    public void PluginDependency_FfmpegAutoGen_UsesHostAssemblyIdentity()
    {
        var path = typeof(ImageCompressionWindow).Assembly.Location;
        var context = new AssemblyLoadContextH(path, "image-compression-sdk-test", new Dictionary<string, string>());
        try
        {
            Assert.AreSame(typeof(FFmpeg.AutoGen.ffmpeg).Assembly,
                context.LoadFromAssemblyName(typeof(FFmpeg.AutoGen.ffmpeg).Assembly.GetName()));
            Assert.AreEqual("PluginCore", typeof(Ffmpeg).Assembly.GetName().Name);
            var feature = typeof(ImageCompressionWindow).GetMethod(nameof(ImageCompressionWindow.Open))!
                .GetCustomAttribute<FeatureAttribute>();
            Assert.IsNotNull(feature);
            Assert.AreEqual("image-compression", feature.Id);
            Assert.AreEqual(FeatureActivationMode.Direct, feature.Activation);
        }
        finally { context.Unload(); }
    }
}
