using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KitopiaEx.BatchRename;
using KitopiaEx.ImageCompression;
using KitopiaEx.SearchWindow.InputDataAnalyzer;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Media;
using PluginCore.SearchWindow.InputData;
using HostKitopia = PluginCore.Kitopia;
using HostLang = Kitopia.Feature.Localization.Lang;

namespace KitopiaTest.Services;

[TestClass]
[DoNotParallelize]
public sealed class BatchRenameWindowTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(1120, 800, "zh-CN", false)]
    [DataRow(960, 600, "en-US", false)]
    [DataRow(1120, 800, "zh-CN", true)]
    public async Task Window_ThemesAndCompactSize_RendersPreviewAndCompressionControls(int width, int height, string language, bool dark)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Kitopia rename UI " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var paths = new[] { "product-photo-with-a-long-file-name.png", "meeting notes.txt", "quarterly-report.pdf" }
                .Select(name => Path.Combine(directory, name)).ToArray();
            foreach (var path in paths) File.WriteAllText(path, "preview");
            var screenshot = Path.Combine(TestContext.TestRunDirectory!, $"batch-rename-without-original-name-{language}-{width}-{dark}.png");
            var compressedScreenshot = Path.Combine(TestContext.TestRunDirectory!, $"batch-rename-compression-{language}-{width}-{dark}.png");
            await using var session = HeadlessUnitTestSession.StartNew(typeof(ImageCompressionWindowTests));
            var rendered = await session.Dispatch(async () =>
            {
                var previousLanguage = HostLang.Current.Language;
                var previousTheme = Application.Current!.RequestedThemeVariant;
                HostLang.Current.RegisterAssembly(typeof(BatchRenameWindow).Assembly);
                HostLang.Current.UseLanguage(language);
                PluginCore.Localization.Lang.Lookup = HostLang.Get;
                PluginCore.Localization.Lang.BindingSource = HostLang.Current;
                Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                using var vm = new BatchRenameViewModel(new Ffmpeg());
                await vm.AddPathsAsync(paths);
                var window = new BatchRenameWindow { DataContext = vm, Width = width, Height = height };
                try
                {
                    window.Show();
                    Dispatcher.UIThread.RunJobs();
                    var list = window.FindControl<ListBox>("FileList")!;
                    Assert.AreEqual(3, list.Items.Count);
                    Assert.IsTrue(list.Bounds.Width >= 500);
                    var sort = window.FindControl<ComboBox>("SortModeComboBox")!;
                    var descending = window.FindControl<ToggleButton>("SortDescendingButton")!;
                    var up = window.FindControl<Button>("MoveUpButton")!;
                    var down = window.FindControl<Button>("MoveDownButton")!;
                    Assert.AreEqual(BatchRenameSortMode.Manual, sort.SelectedItem);
                    Assert.IsFalse(descending.IsEffectivelyEnabled);
                    Assert.IsFalse(up.IsEffectivelyEnabled);
                    Assert.IsFalse(down.IsEffectivelyEnabled);
                    list.SelectedIndex = 1;
                    var selected = vm.SelectedItem;
                    up.Command!.Execute(null);
                    Dispatcher.UIThread.RunJobs();
                    Assert.AreSame(selected, vm.Items[0]);
                    Assert.AreSame(selected, list.SelectedItem);
                    Assert.IsFalse(up.IsEffectivelyEnabled);
                    Assert.IsTrue(down.IsEffectivelyEnabled);
                    down.Command!.Execute(null);
                    sort.SelectedItem = BatchRenameSortMode.Name;
                    Dispatcher.UIThread.RunJobs();
                    Assert.IsTrue(descending.IsEffectivelyEnabled);
                    Assert.AreEqual("meeting notes.txt", vm.Items[0].FileName);
                    descending.IsChecked = true;
                    Dispatcher.UIThread.RunJobs();
                    Assert.AreEqual("quarterly-report.pdf", vm.Items[0].FileName);
                    descending.IsChecked = false;
                    vm.SelectedItem = vm.Items[1];
                    down.Command!.Execute(null);
                    Assert.AreEqual(BatchRenameSortMode.Manual, sort.SelectedItem);
                    sort.SelectedItem = BatchRenameSortMode.Name;
                    vm.SelectedItem = vm.Items[1];
                    up.Command!.Execute(null);
                    // Restore the import order before checking naming and compression settings.
                    Assert.AreEqual(paths[0], vm.Items[0].SourcePath);
                    Assert.AreEqual(paths[1], vm.Items[1].SourcePath);
                    Dispatcher.UIThread.RunJobs();
                    Assert.IsFalse(descending.IsEffectivelyEnabled);
                    var panel = (Control)list.Parent!;
                    foreach (var control in new Control[] { sort, descending, up, down })
                    {
                        var point = control.TranslatePoint(default, panel)!.Value;
                        Assert.IsTrue(point.X >= 0 && point.X + control.Bounds.Width <= panel.Bounds.Width);
                    }
                    var start = window.FindControl<Button>("StartButton")!;
                    Assert.IsTrue(start.IsEffectivelyEnabled);
                    var position = start.TranslatePoint(default, window)!.Value;
                    Assert.IsTrue(position.X >= 0 && position.Y + start.Bounds.Height <= window.ClientSize.Height);
                    var baseName = window.FindControl<TextBox>("BaseNameInput")!;
                    baseName.Text = "report";
                    Assert.AreEqual("report_001.png", vm.Items[0].NewName);
                    Assert.AreEqual("report_002.txt", vm.Items[1].NewName);
                    var keepOriginalName = window.FindControl<CheckBox>("KeepOriginalNameOption")!;
                    var prefix = window.FindControl<TextBox>("PrefixInput")!;
                    var suffix = window.FindControl<TextBox>("SuffixInput")!;
                    var findText = window.FindControl<TextBox>("FindTextInput")!;
                    var replaceText = window.FindControl<TextBox>("ReplaceTextInput")!;
                    var numbering = window.FindControl<CheckBox>("NumberingOption")!;
                    Assert.IsTrue(keepOriginalName.IsChecked);
                    Assert.IsTrue(findText.IsEffectivelyEnabled);
                    Assert.IsTrue(replaceText.IsEffectivelyEnabled);
                    findText.Text = "photo";
                    replaceText.Text = "image";
                    baseName.Text = "";
                    keepOriginalName.IsChecked = false;
                    prefix.Text = "pre-";
                    suffix.Text = "-done";
                    Dispatcher.UIThread.RunJobs();
                    Assert.AreEqual("pre-001-done.png", vm.Items[0].NewName);
                    Assert.AreEqual("pre-002-done.txt", vm.Items[1].NewName);
                    Assert.IsTrue(baseName.IsEffectivelyEnabled);
                    Assert.IsTrue(prefix.IsEffectivelyEnabled);
                    Assert.IsTrue(suffix.IsEffectivelyEnabled);
                    Assert.IsFalse(findText.IsEffectivelyEnabled);
                    Assert.IsFalse(replaceText.IsEffectivelyEnabled);
                    Assert.IsTrue(numbering.IsEffectivelyEnabled);
                    Assert.IsTrue(numbering.IsChecked);
                    numbering.IsChecked = false;
                    Assert.AreEqual("pre--done.png", vm.Items[0].NewName);
                    numbering.IsChecked = true;
                    foreach (var row in list.GetVisualDescendants().OfType<ListBoxItem>())
                    {
                        var remove = row.GetVisualDescendants().OfType<Button>().Single();
                        Assert.IsTrue(remove.TranslatePoint(default, row)!.Value.X + remove.Bounds.Width <= row.Bounds.Width);
                    }
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var frame = window.CaptureRenderedFrame();
                    Assert.IsNotNull(frame);
                    frame.Save(screenshot);
                    keepOriginalName.IsChecked = true;
                    baseName.Text = "report";
                    prefix.Text = "";
                    suffix.Text = "";
                    Dispatcher.UIThread.RunJobs();
                    Assert.AreEqual("report_001.png", vm.Items[0].NewName);
                    Assert.IsTrue(findText.IsEffectivelyEnabled);
                    Assert.IsTrue(replaceText.IsEffectivelyEnabled);
                    Assert.AreEqual("photo", findText.Text);
                    Assert.AreEqual("image", replaceText.Text);
                    var options = window.FindControl<StackPanel>("CompressionOptions")!;
                    Assert.IsFalse(options.IsEffectivelyVisible);
                    window.FindControl<CheckBox>("CompressImagesOption")!.IsChecked = true;
                    Dispatcher.UIThread.RunJobs();
                    Assert.IsTrue(vm.CompressImages);
                    Assert.IsTrue(options.IsEffectivelyVisible);
                    Assert.AreEqual(vm.FfmpegAvailable, start.IsEffectivelyEnabled);
                    var lossless = window.FindControl<CheckBox>("LosslessOption")!;
                    var quality = window.FindControl<Grid>("QualityOptions")!;
                    Assert.IsTrue(lossless.IsEffectivelyVisible);
                    Assert.IsFalse(quality.IsEffectivelyVisible);
                    lossless.IsChecked = false;
                    Dispatcher.UIThread.RunJobs();
                    Assert.IsTrue(quality.IsEffectivelyVisible);
                    vm.Format = ImageCompressionFormat.JPEG;
                    Dispatcher.UIThread.RunJobs();
                    Assert.IsFalse(lossless.IsEffectivelyVisible);
                    Assert.IsTrue(quality.IsEffectivelyVisible);
                    vm.Format = ImageCompressionFormat.Original;
                    options.FindAncestorOfType<ScrollViewer>()!.ScrollToEnd();
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var compressedFrame = window.CaptureRenderedFrame();
                    Assert.IsNotNull(compressedFrame);
                    compressedFrame.Save(compressedScreenshot);
                    vm.AddNumber = false;
                    vm.Items.Add(new BatchRenameItem(paths[0]));
                    vm.RefreshPreview();
                    Assert.IsFalse(start.IsEffectivelyEnabled);
                    vm.ClearCommand.Execute(null);
                    Assert.IsFalse(start.IsEffectivelyEnabled);
                }
                finally
                {
                    window.Close();
                    Application.Current.RequestedThemeVariant = previousTheme;
                    HostLang.Current.UseLanguage(previousLanguage);
                    HostLang.Current.UnregisterAssembly(typeof(BatchRenameWindow).Assembly.GetName().Name!);
                }
                return true;
            }, CancellationToken.None);
            Assert.IsTrue(rendered);
            TestContext.AddResultFile(screenshot);
            TestContext.AddResultFile(compressedScreenshot);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task AnalyzeInputData_Action_ReusesWindowAndImportsDistinctFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Kitopia rename entry " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var originalHost = HostKitopia.ServiceProvider;
        using var services = new ServiceCollection().AddSingleton<Ffmpeg>().BuildServiceProvider();
        HostKitopia.ServiceProvider = services;
        try
        {
            var first = Path.Combine(directory, "first.txt");
            var second = Path.Combine(directory, "second.txt");
            File.WriteAllText(first, "first");
            File.WriteAllText(second, "second");
            var action = new BatchRenameAnalyzer().AnalyzeInputData([
                new InputData { InputType = InputType.文件, Data = first }
            ]).Single();
            var feature = typeof(BatchRenameWindow).GetMethod(nameof(BatchRenameWindow.Open))!.GetCustomAttribute<FeatureAttribute>();
            Assert.IsNotNull(feature);
            Assert.AreEqual("batch-rename", feature.Id);
            Assert.AreEqual(FeatureActivationMode.Direct, feature.Activation);
            await using var session = HeadlessUnitTestSession.StartNew(typeof(ImageCompressionWindowTests));
            var opened = await session.Dispatch(async () =>
            {
                action.Action!(null, null);
                await BatchRenameWindow.OpenFilesAsync([first, second]);
                var window = (BatchRenameWindow)typeof(BatchRenameWindow)
                    .GetProperty("Current", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
                try
                {
                    Assert.AreEqual(2, ((BatchRenameViewModel)window.DataContext!).Items.Count);
                    await BatchRenameWindow.OpenFilesAsync([first]);
                    Assert.AreSame(window, typeof(BatchRenameWindow)
                        .GetProperty("Current", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null));
                }
                finally { window.Close(); }
                return true;
            }, CancellationToken.None);
            Assert.IsTrue(opened);
        }
        finally
        {
            HostKitopia.ServiceProvider = originalHost;
            Directory.Delete(directory, recursive: true);
        }
    }
}
