using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KitopiaEx.ImageCompression;
using KitopiaEx.VideoCompression;
using PluginCore;
using PluginCore.Media;
using HostLang = Kitopia.Feature.Localization.Lang;

namespace KitopiaTest.Services;

[TestClass]
[DoNotParallelize]
public sealed class VideoCompressionWindowTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(1080, 740, "zh-CN", false)]
    [DataRow(900, 600, "en-US", false)]
    [DataRow(1080, 740, "zh-CN", true)]
    public async Task Window_ThemesAndCompactSize_RendersAndBindsControls(int width, int height, string language, bool dark)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ImageCompressionWindowTests));
        await session.Dispatch(() =>
        {
            var originalLanguage = HostLang.Current.Language;
            HostLang.Current.RegisterAssembly(typeof(VideoCompressionWindow).Assembly);
            HostLang.Current.UseLanguage(language);
            PluginCore.Localization.Lang.Lookup = HostLang.Get;
            PluginCore.Localization.Lang.BindingSource = HostLang.Current;
            Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            using var viewModel = new VideoCompressionViewModel(new Ffmpeg());
            foreach (var status in Enum.GetValues<ImageCompressionStatus>())
                viewModel.Items.Add(new ImageCompressionItem(@"C:\Videos\long-video-name-" + status + ".mp4", 12000000, null)
                {
                    Status = status,
                    OutputBytes = status == ImageCompressionStatus.Completed ? 4500000 : null,
                    Error = status == ImageCompressionStatus.Failed ? "Invalid video" : ""
                });
            var window = new VideoCompressionWindow { DataContext = viewModel, Width = width, Height = height };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = window.CaptureRenderedFrame();
                Assert.IsNotNull(frame);
                var screenshot = Path.Combine(TestContext.TestRunDirectory!, $"video-compression-{language}-{width}-{dark}.png");
                frame.Save(screenshot);
                TestContext.AddResultFile(screenshot);
                var list = window.FindControl<ListBox>("FileList")!;
                Assert.AreEqual(7, list.Items.Count);
                Assert.IsTrue(list.Bounds.Width > 400);
                var start = window.FindControl<Button>("StartButton")!;
                Assert.IsTrue(start.IsEffectivelyEnabled);
                var position = start.TranslatePoint(default, window)!.Value;
                Assert.IsTrue(position.X >= 0 && position.X + start.Bounds.Width <= window.Bounds.Width);
                Assert.IsTrue(position.Y >= 0 && position.Y + start.Bounds.Height <= window.Bounds.Height);
                var format = window.FindControl<ComboBox>("FormatComboBox")!;
                Assert.AreEqual(VideoCompressionFormat.MP4, format.SelectedItem);
                Assert.AreEqual(5, format.Items.Count);
                Assert.IsTrue(format.Items.Contains(VideoCompressionFormat.KeepOriginal));
                Assert.IsTrue(format.Items.Contains(VideoCompressionFormat.MOV));
                var codec = window.FindControl<ComboBox>("CodecComboBox")!;
                Assert.AreEqual(VideoCompressionCodec.H264, codec.SelectedItem);
                Assert.AreEqual(5, codec.Items.Count);
                Assert.IsTrue(codec.Items.Contains(VideoCompressionCodec.KeepOriginal));
                Assert.IsTrue(codec.Items.Contains(VideoCompressionCodec.HEVC));
                format.SelectedItem = VideoCompressionFormat.WebM;
                Assert.AreEqual(VideoCompressionFormat.WebM, viewModel.Format);
                Assert.AreEqual(VideoCompressionCodec.VP9, codec.SelectedItem);
                Assert.AreEqual(3, codec.Items.Count);
                Assert.IsFalse(codec.Items.Contains(VideoCompressionCodec.H264));
                Assert.IsFalse(codec.Items.Contains(VideoCompressionCodec.HEVC));
                codec.SelectedItem = VideoCompressionCodec.AV1;
                Assert.AreEqual(VideoCompressionCodec.AV1, viewModel.Codec);
                format.SelectedItem = VideoCompressionFormat.MP4;
                Assert.AreEqual(VideoCompressionCodec.AV1, codec.SelectedItem);
                format.SelectedItem = VideoCompressionFormat.MOV;
                Assert.AreEqual(VideoCompressionCodec.H264, codec.SelectedItem);
                Assert.AreEqual(3, codec.Items.Count);
                Assert.IsFalse(codec.Items.Contains(VideoCompressionCodec.VP9));
                Assert.IsFalse(codec.Items.Contains(VideoCompressionCodec.AV1));
                codec.SelectedItem = VideoCompressionCodec.HEVC;
                var hdr = window.FindControl<CheckBox>("HdrCheckBox")!;
                Assert.IsTrue(hdr.IsChecked);
                hdr.IsChecked = false;
                Assert.IsFalse(viewModel.ConvertHdrToSdr);
                var modes = window.GetVisualDescendants().OfType<RadioButton>().ToArray();
                Assert.AreEqual(3, modes.Length);
                var qualitySettings = window.FindControl<Grid>("QualitySettings")!;
                Assert.IsTrue(qualitySettings.IsVisible);
                Assert.IsTrue(qualitySettings.IsEffectivelyEnabled);
                modes[1].IsChecked = true;
                Assert.AreEqual(VideoCompressionMode.TargetSize, viewModel.Mode);
                Assert.IsFalse(qualitySettings.IsVisible);
                Assert.IsFalse(qualitySettings.IsEffectivelyEnabled);
                modes[2].IsChecked = true;
                Assert.AreEqual(VideoCompressionMode.Conversion, viewModel.Mode);
                Assert.IsTrue(codec.IsEffectivelyEnabled);
                Assert.IsTrue(hdr.IsEffectivelyEnabled);
                Assert.IsFalse(qualitySettings.IsVisible);
                Assert.IsFalse(qualitySettings.IsEffectivelyEnabled);
                Assert.IsFalse(window.FindControl<StackPanel>("SizeSettings")!.IsEffectivelyEnabled);
                Assert.IsFalse(window.FindControl<CheckBox>("KeepAudioCheckBox")!.IsEffectivelyEnabled);
                Assert.IsFalse(window.FindControl<CheckBox>("SkipLargerCheckBox")!.IsEffectivelyEnabled);
                Assert.IsTrue(format.IsEffectivelyEnabled);
                Assert.AreEqual(HostLang.Get("lang.kitopiaex.video_compression.start_conversion"), start.Content);
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var conversionFrame = window.CaptureRenderedFrame();
                var conversionScreenshot = Path.Combine(TestContext.TestRunDirectory!, $"video-conversion-{language}-{width}-{dark}.png");
                conversionFrame!.Save(conversionScreenshot);
                TestContext.AddResultFile(conversionScreenshot);
                codec.SelectedItem = VideoCompressionCodec.KeepOriginal;
                Assert.IsFalse(hdr.IsEffectivelyEnabled);
                Assert.IsFalse(qualitySettings.IsVisible);
                Assert.IsFalse(qualitySettings.IsEffectivelyEnabled);
                format.SelectedItem = VideoCompressionFormat.KeepOriginal;
                Assert.AreEqual(VideoCompressionCodec.KeepOriginal, codec.SelectedItem);
                modes[0].IsChecked = true;
                Assert.IsTrue(qualitySettings.IsVisible);
                Assert.IsTrue(qualitySettings.IsEffectivelyEnabled);
                Assert.IsTrue(codec.IsEffectivelyEnabled);
                Assert.IsTrue(hdr.IsEffectivelyEnabled);
                Assert.IsTrue(window.FindControl<StackPanel>("SizeSettings")!.IsEffectivelyEnabled);
                Assert.IsTrue(window.FindControl<CheckBox>("SkipLargerCheckBox")!.IsEffectivelyEnabled);
                Assert.IsFalse(viewModel.ConvertHdrToSdr);
                viewModel.ClearCommand.Execute(null);
                Assert.IsFalse(start.IsEffectivelyEnabled);
                var feature = typeof(VideoCompressionWindow).GetMethod(nameof(VideoCompressionWindow.Open))!.GetCustomAttribute<FeatureAttribute>();
                Assert.AreEqual("video-compression", feature!.Id);
                Assert.AreEqual(FeatureActivationMode.Direct, feature.Activation);
            }
            finally
            {
                window.Close();
                HostLang.Current.UseLanguage(originalLanguage);
                HostLang.Current.UnregisterAssembly(typeof(VideoCompressionWindow).Assembly.GetName().Name!);
            }
            return true;
        }, CancellationToken.None);
    }
}
