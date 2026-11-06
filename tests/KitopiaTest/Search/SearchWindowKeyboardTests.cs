using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Messaging;
using Kitopia.Desktop.Features.Indexing;
using Kitopia.Desktop.Features.Search;
using Kitopia.Desktop.Features.Search.ProcessUsage;
using Kitopia.Desktop.Features.Search.ViewModels;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Plugin;
using Kitopia.Desktop.Services;
using Kitopia.Desktop.Controls;
using Kitopia.Desktop.Windows;
using KitopiaEx.SearchWindow.InputDataAnalyzer;
using Kitopia.Feature.Localization;
using Microsoft.Extensions.DependencyInjection;
using ObservableCollections;
using PluginCore;
using PluginCore.Config;
using PluginCore.SearchWindow.InputData;
using PluginCore.SearchWindow.InputDataAnalyzer;
using Mat = OpenCvSharp.Mat;
using MatType = OpenCvSharp.MatType;

namespace KitopiaTest.Search;

[TestClass]
[DoNotParallelize]
public sealed class SearchWindowKeyboardTests
{
    public TestContext TestContext { get; set; } = null!;

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Kitopia.Desktop.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia();

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SearchWindow_BorderSetting_AppliesAtCreationAndDuringUseWithoutChangingInternalBorders(bool initiallyHidden)
    {
        await RunAsync((window, _, _) =>
        {
            var config = ConfigManger.Config;
            Assert.AreEqual(initiallyHidden ? WindowDecorations.None : WindowDecorations.BorderOnly, window.WindowDecorations);
            var header = window.FindControl<Border>("SearchHeader")!;
            var input = window.FindControl<TextBox>("tx")!;
            var headerThickness = header.BorderThickness;
            var inputThickness = input.BorderThickness;
            foreach (var hidden in new[] { !initiallyHidden, initiallyHidden })
            {
                config.hideSearchWindowBorder = hidden;
                config.OnConfigChanged(this, nameof(KitopiaConfig.hideSearchWindowBorder), hidden);
                Assert.AreEqual(hidden ? WindowDecorations.None : WindowDecorations.BorderOnly, window.WindowDecorations);
                Assert.AreEqual(headerThickness, header.BorderThickness);
                Assert.AreEqual(inputThickness, input.BorderThickness);
            }
            return Task.CompletedTask;
        }, initiallyHidden);
    }

    [TestMethod]
    [DataRow("Light")]
    [DataRow("Dark")]
    public async Task SearchHeader_LanguageChanges_RefreshesBadgesAndPreservesFilters(string themeName)
    {
        var screenshots = new List<string>();
        await RunAsync(async (window, model, _) =>
        {
            var originalLanguage = Lang.Current.Language;
            try
            {
                var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
                Application.Current!.RequestedThemeVariant = theme;
                window.RequestedThemeVariant = theme;
                model.InputDatas.Add(new InputData { InputType = InputType.文本, Data = "design" });
                var types = new[] { FileType.应用程序, FileType.PDF文档, FileType.Word文档, FileType.PPT文档,
                    FileType.文件, FileType.图像, FileType.窗口, FileType.Excel文档 };
                foreach (var type in types)
                {
                    model.FileTypes.Add(new FileTypeFilter { FileType = type });
                    model.Items.Add(new SearchViewItem { ItemDisplayName = type.ToString(), FileType = type, IconSymbol = 0xF6EC });
                }
                model.ShowFileTypeFilter = true;
                Dispatcher.UIThread.RunJobs();
                var badges = window.FindControl<ItemsControl>("InputTypeBadges")!;
                var filters = window.FindControl<ItemsControl>("FileTypeFilters")!;
                var buttons = filters.GetVisualDescendants().OfType<ToggleButton>().ToArray();
                var badge = badges.GetVisualDescendants().OfType<TextBlock>().Single();
                Assert.AreEqual(12, badge.FontSize);
                Assert.AreEqual(FontWeight.Normal, badge.FontWeight);
                Assert.HasCount(types.Length, buttons);
                Assert.IsTrue(buttons.All(button => button.FontWeight == FontWeight.Normal && button.Height == 28));
                Assert.IsTrue(buttons.All(button => ((ISolidColorBrush)button.Background!).Color == Colors.Transparent));

                var point = buttons[0].TranslatePoint(new Point(12, 12), window)!.Value;
                window.MouseMove(point);
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (model.ItemsView.Count != 1 && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(10);
                    Dispatcher.UIThread.RunJobs();
                }
                Assert.AreEqual(true, buttons[0].IsChecked);
                Assert.IsTrue(model.FileTypes[0].IsChecked);
                Assert.HasCount(1, model.ItemsView);
                window.MouseMove(default);

                foreach (var language in new[] { "zh-CN", "en-US", "zh-CN" })
                {
                    Lang.Current.UseLanguage(language);
                    Dispatcher.UIThread.RunJobs();
                    Assert.AreEqual(language == "en-US" ? "Text" : "文本", badge.Text);
                    Assert.AreEqual(language == "en-US" ? "Applications" : "应用程序", buttons[0].Content);
                    Assert.AreEqual(true, buttons[0].IsChecked);
                    for (var i = 0; i < types.Length; i++)
                        Assert.AreEqual(Lang.Get(types[i]), buttons[i].Content);
                    var surface = buttons[0].GetVisualDescendants().OfType<ContentPresenter>().First();
                    var selected = (ISolidColorBrush)window.FindResource(theme, "KitopiaBadgeCurrentForeground")!;
                    Assert.AreEqual(selected.Color, ((ISolidColorBrush)surface.Foreground!).Color);

                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var frame = window.CaptureRenderedFrame();
                    Assert.IsNotNull(frame);
                    var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/search-keyboard"));
                    Directory.CreateDirectory(directory);
                    var path = Path.Combine(directory, $"search-header-{themeName}-{language}.png");
                    frame.Save(path, PngBitmapEncoderOptions.Default);
                    if (!screenshots.Contains(path)) screenshots.Add(path);
                }

                foreach (var type in Enum.GetValues<InputType>().Where(type => type != InputType.文本))
                    model.InputDatas.Add(new InputData { InputType = type });
                foreach (var type in Enum.GetValues<FileType>().Except(types))
                    model.FileTypes.Add(new FileTypeFilter { FileType = type });
                Lang.Current.UseLanguage("en-US");
                Dispatcher.UIThread.RunJobs();
                CollectionAssert.AreEquivalent(Enum.GetValues<InputType>().Select(type => Lang.Get(type)).ToArray(),
                    badges.GetVisualDescendants().OfType<TextBlock>().Select(label => label.Text).ToArray());
                CollectionAssert.AreEquivalent(Enum.GetValues<FileType>().Select(type => Lang.Get(type)).ToArray(),
                    filters.GetVisualDescendants().OfType<ToggleButton>().Select(button => (string)button.Content!).ToArray());
                var scroll = filters.GetVisualAncestors().OfType<ScrollViewer>().First();
                Assert.IsGreaterThan(scroll.Viewport.Width, scroll.Extent.Width);
                Assert.AreEqual(28, filters.Bounds.Height);
                scroll.Offset = new Vector(scroll.Extent.Width, 0);
                Dispatcher.UIThread.RunJobs();
                Assert.IsGreaterThan(0, scroll.Offset.X);
                var badgeScroll = badges.GetVisualAncestors().OfType<ScrollViewer>().First();
                Assert.IsLessThanOrEqualTo(240, badgeScroll.Bounds.Width);
                Assert.IsGreaterThan(badgeScroll.Viewport.Width, badgeScroll.Extent.Width);
                badgeScroll.Offset = new Vector(badgeScroll.Extent.Width, 0);
                Dispatcher.UIThread.RunJobs();
                Assert.IsGreaterThan(0, badgeScroll.Offset.X);
                model.DirectorySearchPath = "C:\\Documents";
                Dispatcher.UIThread.RunJobs();
                Assert.IsFalse(badges.IsEffectivelyVisible);
            }
            finally
            {
                Lang.Current.UseLanguage(originalLanguage);
            }
        });
        foreach (var screenshot in screenshots) TestContext.AddResultFile(screenshot);
    }

    [TestMethod]
    [DataRow("Light")]
    [DataRow("Dark")]
    public async Task Selection_ResultAndShortcut_AlwaysMatchesVisibleHighlight(string themeName)
    {
        string? screenshot = null;
        await RunAsync((window, model, _) =>
        {
            var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
            Application.Current!.RequestedThemeVariant = theme;
            window.RequestedThemeVariant = theme;
            using var themeService = new ThemeChange();
            themeService.SetAccentColor(false, "#009688");
            var input = window.FindControl<TextBox>("tx")!;
            var results = window.FindControl<ListBox>("dataGrid")!;
            var shortcuts = window.FindControl<ListBox>("PinnedList")!;
            model.PinnedItems.Add(new SearchViewItem { ItemDisplayName = "Extract text", IconSymbol = 0xF6EC });
            model.ShowPinnedItems = true;
            model.Items.Add(new SearchViewItem { ItemDisplayName = "First result", IconSymbol = 0xF6EC });
            model.Items.Add(new SearchViewItem { ItemDisplayName = "Second result", IconSymbol = 0xF6EC });
            Dispatcher.UIThread.RunJobs();
            input.Focus();

            var first = (ListBoxItem)results.ContainerFromIndex(0)!;
            var second = (ListBoxItem)results.ContainerFromIndex(1)!;
            var shortcut = (ListBoxItem)shortcuts.ContainerFromIndex(0)!;
            var firstSurface = first.GetVisualChildren().OfType<ContentPresenter>().Single();
            var secondSurface = second.GetVisualChildren().OfType<ContentPresenter>().Single();
            var shortcutSurface = shortcut.GetVisualChildren().OfType<ContentPresenter>().Single();
            var primary = ((ISolidColorBrush)window.FindResource(theme, "SemiColorPrimary")!).Color;
            var fill = ((ISolidColorBrush)window.FindResource(theme, "SemiColorFill1")!).Color;

            Assert.IsTrue(input.IsFocused);
            Assert.AreEqual(-1, results.SelectedIndex);
            Assert.IsNull(model.SelectedItem);
            Assert.IsFalse(first.IsSelected);
            Assert.AreEqual(Colors.Transparent, ((ISolidColorBrush)firstSurface.BorderBrush!).Color);

            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.IsTrue(first.IsSelected);
            Assert.AreEqual(primary, ((ISolidColorBrush)firstSurface.BorderBrush!).Color);
            Assert.AreEqual(fill, ((ISolidColorBrush)firstSurface.Background!).Color);
            Assert.AreEqual(Colors.Transparent, ((ISolidColorBrush)secondSurface.BorderBrush!).Color);
            Assert.AreEqual(Colors.Transparent, ((ISolidColorBrush)shortcutSurface.BorderBrush!).Color);
            Assert.AreEqual(new Thickness(1), firstSurface.BorderThickness);
            Assert.AreEqual(new CornerRadius(6), firstSurface.CornerRadius);

            var hoverPoint = second.TranslatePoint(new Point(100, second.Bounds.Height / 2), window)!.Value;
            window.MouseMove(hoverPoint);
            Dispatcher.UIThread.RunJobs();
            Assert.IsFalse(second.IsSelected);
            Assert.AreEqual(Colors.Transparent, ((ISolidColorBrush)secondSurface.BorderBrush!).Color);
            Assert.AreEqual(primary, ((ISolidColorBrush)firstSurface.BorderBrush!).Color);
            window.MouseMove(default);

            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = window.CaptureRenderedFrame();
            Assert.IsNotNull(frame);
            var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/search-keyboard"));
            Directory.CreateDirectory(directory);
            screenshot = Path.Combine(directory, $"search-selection-{themeName}.png");
            frame.Save(screenshot, PngBitmapEncoderOptions.Default);

            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            Assert.IsFalse(first.IsSelected);
            Assert.IsTrue(shortcut.IsSelected);
            Assert.AreEqual(Colors.Transparent, ((ISolidColorBrush)firstSurface.BorderBrush!).Color);
            Assert.AreEqual(Colors.Transparent, ((ISolidColorBrush)firstSurface.Background!).Color);
            Assert.AreEqual(primary, ((ISolidColorBrush)shortcutSurface.BorderBrush!).Color);

            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.IsTrue(first.IsSelected);
            Assert.IsFalse(shortcut.IsSelected);
            Assert.AreEqual(primary, ((ISolidColorBrush)firstSurface.BorderBrush!).Color);
            Assert.AreEqual(Colors.Transparent, ((ISolidColorBrush)shortcutSurface.BorderBrush!).Color);
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.AreSame(model.Items[1], model.SelectedItem);
            Assert.IsTrue(second.IsSelected);
            Assert.AreEqual(Colors.Transparent, ((ISolidColorBrush)firstSurface.BorderBrush!).Color);
            Assert.AreEqual(primary, ((ISolidColorBrush)secondSurface.BorderBrush!).Color);
            results.SelectedIndex = -1;
            Assert.IsFalse(second.IsSelected);
            Assert.AreEqual(Colors.Transparent, ((ISolidColorBrush)secondSurface.BorderBrush!).Color);
            return Task.CompletedTask;
        });
        TestContext.AddResultFile(screenshot!);
    }

    [TestMethod]
    [DataRow(FileType.URL, false)]
    [DataRow(FileType.Word文档, true)]
    public async Task Search_ResultsArrive_SelectsFirstResultAndEnterOpensIt(FileType fileType, bool preview)
    {
        await RunAsync(async (window, model, services) =>
        {
            var input = window.FindControl<TextBox>("tx")!;
            var results = window.FindControl<ListBox>("dataGrid")!;
            var shortcuts = window.FindControl<ListBox>("PinnedList")!;
            var index = (SearchServicesProxy)ServiceManager.Services.GetRequiredService<IIndexService>();
            index.Results = Enumerable.Range(0, 3).Select(i => new SearchIndexResult(new SearchEntry
            {
                OnlyKey = $"https://example.com/document-{i}", DisplayName = $"Document {i}", FileType = fileType
            }, 1, null)).ToArray();
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            model.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(model.IsSemanticSearching) && !model.IsSemanticSearching)
                    completed.TrySetResult();
            };

            model.Search = "query";
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            Assert.HasCount(3, results.Items);
            Assert.AreEqual(preview, model.IsPreviewMode);
            Assert.IsTrue(input.IsFocused);
            if (preview)
            {
                Assert.AreEqual(0, results.SelectedIndex);
                Assert.AreEqual(-1, shortcuts.SelectedIndex);
                Assert.AreSame(model.Items[0], model.SelectedItem);
                Assert.AreEqual(model.Items[0].OnlyKey, model.FilePreview.SelectedPath);

                foreach (var showDocument in new[] { true, false })
                {
                    model.ItemsView.AttachFilter(item => showDocument && ReferenceEquals(item, model.Items[2]));
                    Dispatcher.UIThread.RunJobs();
                    model.TogglePreviewModeCommand.Execute(null);
                    model.TogglePreviewModeCommand.Execute(null);
                    Dispatcher.UIThread.RunJobs();
                    var visible = showDocument ? model.Items[2] : null;
                    Assert.AreSame(visible, model.SelectedItem);
                    Assert.AreSame(visible, results.SelectedItem);
                    Assert.AreEqual(visible?.OnlyKey ?? "", model.FilePreview.SelectedPath);
                }
                model.ItemsView.ResetFilter();
                Dispatcher.UIThread.RunJobs();
                model.TogglePreviewModeCommand.Execute(null);
                model.TogglePreviewModeCommand.Execute(null);
                Assert.AreSame(model.Items[0], model.SelectedItem);

                var expected = model.Items[1];
                results.SelectedItem = expected;
                model.TogglePreviewModeCommand.Execute(null);
                Assert.IsFalse(model.IsPreviewMode);
                model.TogglePreviewModeCommand.Execute(null);
                Assert.IsTrue(model.IsPreviewMode);
                Assert.AreSame(expected, model.SelectedItem);

                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                Assert.AreSame(model.Items[2], model.SelectedItem);
                Assert.IsTrue(results.IsKeyboardFocusWithin);
                window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
                Assert.AreSame(expected, model.SelectedItem);

                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Assert.AreSame(expected, await services.Opened.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            }
            else
            {
                Assert.AreEqual(0, results.SelectedIndex);
                Assert.AreEqual(-1, shortcuts.SelectedIndex);
                Assert.AreSame(model.Items[0], model.SelectedItem);
                Assert.AreEqual("", model.FilePreview.SelectedPath);

                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                Assert.AreEqual(1, results.SelectedIndex);
                Assert.IsTrue(results.IsKeyboardFocusWithin);
                window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
                Assert.AreEqual(0, results.SelectedIndex);
                window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
                Dispatcher.UIThread.RunJobs();
                Assert.IsTrue(input.IsFocused);
                Assert.AreEqual(0, results.SelectedIndex);
                Assert.AreSame(model.Items[0], model.SelectedItem);
                Assert.AreEqual("", model.FilePreview.SelectedPath);

                model.Items.Add(new SearchViewItem { ItemDisplayName = "Later result", IconSymbol = 0xF6EC });
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(0, results.SelectedIndex);
                var expected = model.Items[0];
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Assert.AreSame(expected, await services.Opened.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            }
        });
    }

    [TestMethod]
    public async Task Search_FilterChanges_SelectsFirstVisibleAndEmptyResultsDoNotOpen()
    {
        await RunAsync(async (window, model, services) =>
        {
            var index = (SearchServicesProxy)ServiceManager.Services.GetRequiredService<IIndexService>();
            index.Results = new[] { FileType.URL, FileType.数学运算 }.Select((type, i) =>
                new SearchIndexResult(new SearchEntry
                {
                    OnlyKey = $"result-{i}", DisplayName = $"Result {i}", FileType = type
                }, 1, null)).ToArray();
            model.Search = "q";
            for (var i = 0; i < 100 && model.Items.Count != 2; i++)
            {
                await Task.Delay(20);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.HasCount(2, model.Items);
            var expected = model.Items[1];
            model.FileTypes.Add(new FileTypeFilter { FileType = FileType.图像, IsChecked = true });
            model.UpdateFilter();
            Dispatcher.UIThread.RunJobs();
            Assert.IsNull(model.SelectedItem);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.IsFalse(services.Opened.Task.IsCompleted);

            foreach (var filter in model.FileTypes) filter.IsChecked = filter.FileType == expected.FileType;
            model.UpdateFilter();
            Dispatcher.UIThread.RunJobs();
            Assert.AreSame(expected, model.SelectedItem);
            Assert.AreSame(expected, window.FindControl<ListBox>("dataGrid")!.SelectedItem);
            Assert.IsTrue(window.FindControl<TextBox>("tx")!.IsFocused);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.AreSame(expected, await services.Opened.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        });
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task InputRegainsFocus_ShortcutOrResultSelected_RestoresDefaultAndEnterOpensIt(bool fromShortcuts)
    {
        await RunAsync(async (window, model, services) =>
        {
            var input = window.FindControl<TextBox>("tx")!;
            var results = window.FindControl<ListBox>("dataGrid")!;
            var shortcuts = window.FindControl<ListBox>("PinnedList")!;
            model.PinnedItems.Add(new SearchViewItem { ItemDisplayName = "Shortcut", IconSymbol = 0xF6EC });
            model.ShowPinnedItems = fromShortcuts;
            model.Items.Add(new SearchViewItem { ItemDisplayName = "Result", IconSymbol = 0xF6EC });
            Dispatcher.UIThread.RunJobs();
            input.Focus();
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.AreEqual(0, (fromShortcuts ? shortcuts : results).SelectedIndex);
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            Assert.IsTrue(input.IsFocused);
            Assert.AreEqual(fromShortcuts ? -1 : 0, results.SelectedIndex);
            Assert.AreEqual(fromShortcuts ? 0 : -1, shortcuts.SelectedIndex);
            var expected = fromShortcuts ? model.PinnedItems[0] : model.ItemsViewList[0];
            Assert.AreSame(expected, model.SelectedPinnedItem ?? model.SelectedItem);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.AreSame(expected, await services.Opened.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        });
    }

    [TestMethod]
    [DataRow("empty", false)]
    [DataRow("empty", true)]
    [DataRow("ordinary", false)]
    [DataRow("ordinary", true)]
    [DataRow("preview", false)]
    [DataRow("preview", true)]
    public async Task InputRegainsFocus_AllListModes_UseSameDefaultAndAdvanceOnDown(string mode, bool hasPinnedItems)
    {
        await RunAsync(async (window, model, services) =>
        {
            var pinned1 = new SearchViewItem { ItemDisplayName = "Pinned 1", IconSymbol = 0xF6EC, ShowAsMiniApp = true };
            var pinned2 = new SearchViewItem { ItemDisplayName = "Pinned 2", IconSymbol = 0xF6EC, ShowAsMiniApp = true };
            var first = new SearchViewItem { ItemDisplayName = "First", IconSymbol = 0xF6EC };
            var second = new SearchViewItem { ItemDisplayName = "Second", IconSymbol = 0xF6EC };
            PluginOverall.SearchWindowInputDataAnalyzers["Kitopia"] =
            [
                (() => InputDataAnalyzeTimeFlags.InputEmpty | InputDataAnalyzeTimeFlags.InputChanged,
                    _ => mode == "empty"
                        ? (hasPinnedItems ? [pinned2, pinned1, second, first] : [second, first])
                        : (hasPinnedItems ? [pinned2, pinned1] : []))
            ];
            if (mode == "empty")
                model.LoadLast();
            else
            {
                var index = (SearchServicesProxy)ServiceManager.Services.GetRequiredService<IIndexService>();
                index.Results = Enumerable.Range(0, 2).Select(i => new SearchIndexResult(new SearchEntry
                {
                    OnlyKey = $"result-{i}", DisplayName = $"Result {i}",
                    FileType = mode == "preview" ? FileType.Word文档 : FileType.URL
                }, 1, null)).ToArray();
                model.Search = "query";
                for (var i = 0; i < 100 && model.Items.Count != 2; i++)
                {
                    await Task.Delay(20);
                    Dispatcher.UIThread.RunJobs();
                }
                if (mode == "preview") model.TogglePreviewModeCommand.Execute(null);
            }
            Dispatcher.UIThread.RunJobs();
            Assert.HasCount(2, model.ItemsViewList);
            Assert.AreEqual(mode == "preview", model.IsPreviewMode);
            var input = window.FindControl<TextBox>("tx")!;
            var results = window.FindControl<ListBox>("dataGrid")!;
            var shortcuts = window.FindControl<ListBox>("PinnedList")!;
            var defaultItem = hasPinnedItems ? pinned1 : model.ItemsViewList[0];
            Assert.IsTrue(input.IsFocused);
            Assert.AreSame(defaultItem, model.SelectedPinnedItem ?? model.SelectedItem);

            // Returning by either input focus or Up uses the same default in every mode.
            results.SelectedIndex = 1;
            results.ContainerFromIndex(1)!.Focus();
            input.Focus();
            Assert.IsTrue(input.IsFocused);
            Assert.AreSame(defaultItem, model.SelectedPinnedItem ?? model.SelectedItem);
            Assert.AreEqual(hasPinnedItems ? -1 : 0, results.SelectedIndex);
            Assert.AreEqual(hasPinnedItems ? 0 : -1, shortcuts.SelectedIndex);

            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.AreSame(hasPinnedItems ? pinned2 : model.ItemsViewList[1], model.SelectedPinnedItem ?? model.SelectedItem);
            Assert.IsTrue((hasPinnedItems ? shortcuts : results).IsKeyboardFocusWithin);
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            Assert.IsTrue(input.IsFocused);
            Assert.AreSame(defaultItem, model.SelectedPinnedItem ?? model.SelectedItem);

            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.AreSame(defaultItem, await services.Opened.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        });
    }

    [TestMethod]
    public async Task Selection_ViewAndFilterChanges_PreserveValidItemAndUseSameDefaultWhenInvalid()
    {
        await RunAsync(async (window, model, _) =>
        {
            var index = (SearchServicesProxy)ServiceManager.Services.GetRequiredService<IIndexService>();
            index.Results = new[] { FileType.URL, FileType.Word文档, FileType.Word文档 }.Select((type, i) =>
                new SearchIndexResult(new SearchEntry { OnlyKey = $"result-{i}", DisplayName = $"Result {i}", FileType = type }, 1, null)).ToArray();
            model.Search = "query";
            for (var i = 0; i < 100 && model.Items.Count != 3; i++)
            {
                await Task.Delay(20);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.HasCount(3, model.ItemsViewList);
            var first = model.ItemsViewList[0];
            var selected = model.ItemsViewList[2];
            model.TogglePreviewModeCommand.Execute(null);
            Assert.IsTrue(model.IsPreviewMode);
            Assert.AreSame(first, model.SelectedItem);
            model.SelectedItem = selected;
            model.TogglePreviewModeCommand.Execute(null);
            model.TogglePreviewModeCommand.Execute(null);
            Assert.AreSame(selected, model.SelectedItem);

            var input = window.FindControl<TextBox>("tx")!;
            var results = window.FindControl<ListBox>("dataGrid")!;
            results.ContainerFromIndex(2)!.Focus();
            input.Focus();
            Assert.AreSame(first, model.SelectedItem);
            Assert.AreEqual("", model.FilePreview.SelectedPath);

            model.SelectedItem = selected;
            foreach (var filter in model.FileTypes) filter.IsChecked = filter.FileType == FileType.Word文档;
            model.UpdateFilter();
            Assert.AreSame(selected, model.SelectedItem);
            model.UpdateSelection(resetToDefault: true);
            Assert.AreSame(model.ItemsViewList[0], model.SelectedItem);
            Assert.AreNotSame(first, model.SelectedItem);

            var pinned1 = new SearchViewItem { ItemDisplayName = "Pinned 1", IconSymbol = 0xF6EC };
            var pinned2 = new SearchViewItem { ItemDisplayName = "Pinned 2", IconSymbol = 0xF6EC };
            model.PinnedItems.Add(pinned1);
            model.PinnedItems.Add(pinned2);
            model.ShowPinnedItems = true;
            model.UpdateSelection(resetToDefault: true);
            Assert.AreSame(pinned1, model.SelectedPinnedItem);
            model.SelectedPinnedItem = pinned2;
            model.UpdateFilter();
            Assert.AreSame(pinned2, model.SelectedPinnedItem);
            model.PinnedItems.Remove(pinned2);
            model.UpdateSelection();
            Assert.AreSame(pinned1, model.SelectedPinnedItem);
            model.ShowPinnedItems = false;
            model.UpdateSelection();
            Assert.IsNull(model.SelectedPinnedItem);
            Assert.AreSame(model.ItemsViewList[0], model.SelectedItem);

            model.ItemsView.AttachFilter(_ => false);
            model.UpdateSelection(resetToDefault: true);
            Assert.IsNull(model.SelectedItem);
            Assert.IsNull(model.SelectedPinnedItem);
        });
    }

    [TestMethod]
    public async Task ArrowKeys_ShortcutsAndResults_KeepOneSelectionAndRestoreShortcut()
    {
        await RunAsync((window, model, _) =>
        {
            var input = window.FindControl<TextBox>("tx")!;
            var shortcuts = window.FindControl<ListBox>("PinnedList")!;
            var results = window.FindControl<ListBox>("dataGrid")!;
            model.PinnedItems.Add(new SearchViewItem { ItemDisplayName = "Extract text", IconSymbol = 0xF6EC });
            model.PinnedItems.Add(new SearchViewItem { ItemDisplayName = "Compress image", IconSymbol = 0xF6EC });
            model.ShowPinnedItems = true;
            model.Items.Add(new SearchViewItem { ItemDisplayName = "Result", IconSymbol = 0xF6EC });
            Dispatcher.UIThread.RunJobs();
            input.Focus();

            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.AreEqual(0, shortcuts.SelectedIndex);
            Assert.IsTrue(shortcuts.IsKeyboardFocusWithin);
            Assert.AreEqual(-1, results.SelectedIndex);

            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            Assert.AreEqual(1, shortcuts.SelectedIndex);
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.AreEqual(0, results.SelectedIndex);
            Assert.IsTrue(results.IsKeyboardFocusWithin);
            Assert.AreEqual(-1, shortcuts.SelectedIndex);

            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            Assert.AreEqual(1, shortcuts.SelectedIndex);
            Assert.IsTrue(shortcuts.IsKeyboardFocusWithin);
            Assert.AreEqual(-1, results.SelectedIndex);
            window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
            Assert.AreEqual(0, shortcuts.SelectedIndex);
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            Assert.IsTrue(input.IsFocused);
            Assert.AreEqual(0, shortcuts.SelectedIndex);
            Assert.AreEqual(-1, results.SelectedIndex);
            Assert.IsNull(model.SelectedItem);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task ArrowKeys_PinnedHistoryItems_AllowNavigationWithUpDownAndLeftRight()
    {
        await RunAsync((window, model, _) =>
        {
            var input = window.FindControl<TextBox>("tx")!;
            var shortcuts = window.FindControl<ListBox>("PinnedList")!;
            var results = window.FindControl<ListBox>("dataGrid")!;
            model.PinnedItems.Add(new SearchViewItem { ItemDisplayName = "History 1", IconSymbol = 0xF6EC });
            model.PinnedItems.Add(new SearchViewItem { ItemDisplayName = "History 2", IconSymbol = 0xF6EC });
            model.PinnedItems.Add(new SearchViewItem { ItemDisplayName = "History 3", IconSymbol = 0xF6EC });
            model.ShowPinnedItems = true;
            Dispatcher.UIThread.RunJobs();
            input.Focus();

            // Down from input enters PinnedList at index 0
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.AreEqual(0, shortcuts.SelectedIndex);
            Assert.IsTrue(shortcuts.IsKeyboardFocusWithin);

            // Down navigates forward through history items
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.AreEqual(1, shortcuts.SelectedIndex);
            Assert.IsTrue(shortcuts.IsKeyboardFocusWithin);

            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.AreEqual(2, shortcuts.SelectedIndex);
            Assert.IsTrue(shortcuts.IsKeyboardFocusWithin);

            // At the last history item with no results below, Down loops back to the first history item
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.AreEqual(0, shortcuts.SelectedIndex);
            Assert.IsTrue(shortcuts.IsKeyboardFocusWithin);

            // Up from index 0 returns focus to input
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            Assert.IsTrue(input.IsFocused);
            Assert.AreEqual(0, shortcuts.SelectedIndex);

            // Left and Right keys still switch items
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.AreEqual(1, shortcuts.SelectedIndex);

            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            Assert.AreEqual(2, shortcuts.SelectedIndex);

            window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
            Assert.AreEqual(1, shortcuts.SelectedIndex);

            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.AreEqual(2, shortcuts.SelectedIndex);

            window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
            Assert.AreEqual(1, shortcuts.SelectedIndex);

            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            Assert.AreEqual(0, shortcuts.SelectedIndex);

            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            Assert.IsTrue(input.IsFocused);

            // Test cyclical navigation: input -> history list -> results list -> history list -> results list
            model.Items.Add(new SearchViewItem { ItemDisplayName = "Result 1", IconSymbol = 0xF6EC });
            model.Items.Add(new SearchViewItem { ItemDisplayName = "Result 2", IconSymbol = 0xF6EC });
            Dispatcher.UIThread.RunJobs();

            // 1. From input down to history list
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.AreEqual(1, shortcuts.SelectedIndex);
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.AreEqual(2, shortcuts.SelectedIndex);

            // 2. From last history item down to results list
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.AreEqual(0, results.SelectedIndex);
            Assert.IsTrue(results.IsKeyboardFocusWithin);
            Assert.AreEqual(-1, shortcuts.SelectedIndex);

            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.AreEqual(1, results.SelectedIndex);
            Assert.IsTrue(results.IsKeyboardFocusWithin);

            // 3. Cycle: from last result item down loops back to history list
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.AreEqual(0, shortcuts.SelectedIndex);
            Assert.IsTrue(shortcuts.IsKeyboardFocusWithin);
            Assert.AreEqual(-1, results.SelectedIndex);

            // 4. Continue cycle through history and back to results
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.AreEqual(1, shortcuts.SelectedIndex);
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.AreEqual(2, shortcuts.SelectedIndex);
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.AreEqual(0, results.SelectedIndex);
            Assert.IsTrue(results.IsKeyboardFocusWithin);

            // Up from results index 0 moves back to history list last item
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            Assert.AreEqual(2, shortcuts.SelectedIndex);
            Assert.IsTrue(shortcuts.IsKeyboardFocusWithin);
            Assert.AreEqual(-1, results.SelectedIndex);

            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            Assert.AreEqual(1, shortcuts.SelectedIndex);
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            Assert.AreEqual(0, shortcuts.SelectedIndex);
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            Assert.IsTrue(input.IsFocused);
            Assert.AreEqual(0, shortcuts.SelectedIndex);
            Assert.AreEqual(-1, results.SelectedIndex);

            return Task.CompletedTask;
        });
    }

    [TestMethod]
    [DataRow(true, true)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(false, false)]
    public async Task DefaultSelection_LoadLast_PrefersPinnedThenResultsAndEnterOpensIt(bool hasPinnedItems, bool navigateBeforeEnter)
    {
        await RunAsync(async (window, model, services) =>
        {
            var input = window.FindControl<TextBox>("tx")!;
            var shortcuts = window.FindControl<ListBox>("PinnedList")!;
            var results = window.FindControl<ListBox>("dataGrid")!;
            var pinned1 = new SearchViewItem { ItemDisplayName = "Pinned 1", OnlyKey = "key1", IconSymbol = 0xF6EC, ShowAsMiniApp = true };
            var pinned2 = new SearchViewItem { ItemDisplayName = "Pinned 2", OnlyKey = "key2", IconSymbol = 0xF6EC, ShowAsMiniApp = true };
            var result = new SearchViewItem { ItemDisplayName = "Result 1", OnlyKey = "result1", IconSymbol = 0xF6EC };
            var secondResult = new SearchViewItem { ItemDisplayName = "Result 2", OnlyKey = "result2", IconSymbol = 0xF6EC };
            PluginOverall.SearchWindowInputDataAnalyzers["Kitopia"] =
            [
                (() => InputDataAnalyzeTimeFlags.InputEmpty, _ => hasPinnedItems ? [pinned2, pinned1, secondResult, result] : [secondResult, result])
            ];
            model.LoadLast();
            Dispatcher.UIThread.RunJobs();

            Assert.IsTrue(input.IsFocused);
            Assert.AreSame(hasPinnedItems ? pinned1 : null, model.SelectedPinnedItem);
            Assert.AreSame(hasPinnedItems ? pinned1 : null, shortcuts.SelectedItem);
            Assert.AreEqual(hasPinnedItems ? 0 : -1, shortcuts.SelectedIndex);
            Assert.AreSame(hasPinnedItems ? null : result, model.SelectedItem);
            Assert.AreEqual(hasPinnedItems ? -1 : 0, results.SelectedIndex);

            if (navigateBeforeEnter)
            {
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                var list = hasPinnedItems ? shortcuts : results;
                Assert.AreEqual(1, list.SelectedIndex);
                Assert.IsTrue(list.IsKeyboardFocusWithin);
                if (hasPinnedItems)
                {
                    Assert.AreSame(pinned2, shortcuts.SelectedItem);
                    window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                    Assert.AreSame(result, results.SelectedItem);
                    window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
                    Assert.AreSame(pinned2, shortcuts.SelectedItem);
                }
            }
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.AreSame(hasPinnedItems ? (navigateBeforeEnter ? pinned2 : pinned1) : (navigateBeforeEnter ? secondResult : result),
                await services.Opened.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        });
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task DefaultSelection_SingleItem_DownUsesListBoundary(bool hasPinnedItem)
    {
        await RunAsync((window, model, _) =>
        {
            var pinned = new SearchViewItem { ItemDisplayName = "Pinned", IconSymbol = 0xF6EC, ShowAsMiniApp = true };
            var result = new SearchViewItem { ItemDisplayName = "Result", IconSymbol = 0xF6EC };
            PluginOverall.SearchWindowInputDataAnalyzers["Kitopia"] =
            [
                (() => InputDataAnalyzeTimeFlags.InputEmpty, _ => hasPinnedItem ? [pinned, result] : [result])
            ];
            model.LoadLast();
            Dispatcher.UIThread.RunJobs();
            Assert.IsTrue(window.FindControl<TextBox>("tx")!.IsFocused);

            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            var results = window.FindControl<ListBox>("dataGrid")!;
            Assert.AreSame(result, results.SelectedItem);
            Assert.IsTrue(results.IsKeyboardFocusWithin);

            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            if (hasPinnedItem)
            {
                var shortcuts = window.FindControl<ListBox>("PinnedList")!;
                Assert.AreSame(pinned, shortcuts.SelectedItem);
                Assert.IsTrue(shortcuts.IsKeyboardFocusWithin);
                Assert.IsNull(model.SelectedItem);
            }
            else
            {
                Assert.AreSame(result, results.SelectedItem);
                Assert.IsTrue(results.IsKeyboardFocusWithin);
            }
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task ArrowKeys_EmptyAndFilteredResults_UseVisibleItemsWithoutThrowing()
    {
        await RunAsync((window, model, _) =>
        {
            var input = window.FindControl<TextBox>("tx")!;
            var shortcuts = window.FindControl<ListBox>("PinnedList")!;
            var results = window.FindControl<ListBox>("dataGrid")!;
            input.Focus();
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.IsTrue(input.IsFocused);

            model.PinnedItems.Add(new SearchViewItem { ItemDisplayName = "Shortcut", IconSymbol = 0xF6EC });
            model.ShowPinnedItems = true;
            Dispatcher.UIThread.RunJobs();
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.AreEqual(0, shortcuts.SelectedIndex);
            Assert.IsTrue(shortcuts.IsKeyboardFocusWithin);

            model.PinnedItems.Clear();
            model.ShowPinnedItems = false;
            model.Items.Add(new SearchViewItem { ItemDisplayName = "Excluded", FileType = FileType.URL, IconSymbol = 0xF6EC });
            var visible = new SearchViewItem { ItemDisplayName = "Visible", FileType = FileType.数学运算, IconSymbol = 0xF6EC };
            model.Items.Add(visible);
            model.FileTypes.Add(new FileTypeFilter { FileType = visible.FileType, IsChecked = true });
            model.UpdateFilter();
            Dispatcher.UIThread.RunJobs();
            input.Focus();
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.AreSame(visible, results.SelectedItem);
            Assert.IsTrue(results.IsKeyboardFocusWithin);
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            Assert.IsTrue(input.IsFocused);
            Assert.AreEqual(0, results.SelectedIndex);
            Assert.AreSame(visible, model.SelectedItem);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    [DataRow("shortcut")]
    [DataRow("mouse")]
    public async Task Activate_SelectedShortcut_ExecutesShortcut(string source)
    {
        await RunAsync(async (window, model, services) =>
        {
            var shortcut = new SearchViewItem { ItemDisplayName = "Shortcut", IconSymbol = 0xF6EC };
            model.PinnedItems.Add(shortcut);
            model.ShowPinnedItems = true;
            model.Items.Add(new SearchViewItem { ItemDisplayName = "Result", IconSymbol = 0xF6EC });
            Dispatcher.UIThread.RunJobs();
            window.FindControl<TextBox>("tx")!.Focus();
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            if (source == "mouse")
            {
                var button = window.FindControl<ListBox>("PinnedList")!.ContainerFromIndex(0)!
                    .GetVisualDescendants().OfType<Button>().First();
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
            }
            else
            {
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            }
            Assert.AreSame(shortcut, await services.Opened.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        });
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task EditInput_ListFocused_PreservesFirstCharacterAndSupportsPaste(bool fromShortcuts)
    {
        await RunAsync(async (window, model, _) =>
        {
            var input = window.FindControl<TextBox>("tx")!;
            var list = window.FindControl<ListBox>(fromShortcuts ? "PinnedList" : "dataGrid")!;
            list.ItemsSource = new[] { new SearchViewItem { ItemDisplayName = "Item", IconSymbol = 0xF6EC } };
            model.ShowPinnedItems = fromShortcuts;
            Dispatcher.UIThread.RunJobs();
            list.SelectedIndex = 0;
            list.ContainerFromIndex(0)!.Focus();

            window.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.A, "a");
            window.KeyTextInput("a");
            Assert.AreEqual("a", input.Text);
            Assert.IsTrue(input.IsFocused);
            input.CaretIndex = 1;
            list.ContainerFromIndex(0)!.Focus();
            window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
            Assert.AreEqual("", input.Text);

            list.ContainerFromIndex(0)!.Focus();
            window.KeyTextInput("中文");
            Assert.AreEqual("中文", input.Text);
            Assert.IsTrue(input.IsFocused);

            await window.Clipboard!.SetTextAsync("pasted");
            input.SelectAll();
            list.ContainerFromIndex(0)!.Focus();
            window.KeyPress(Key.V, RawInputModifiers.Control, PhysicalKey.V, "v");
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual("pasted", input.Text);
            Assert.IsTrue(input.IsFocused);
        });
    }

    [TestMethod]
    [DataRow("Light")]
    [DataRow("Dark")]
    public async Task SearchLabels_LanguageChanges_RefreshesImageActionsAndResultTypes(string themeName)
    {
        var screenshots = new List<string>();
        await RunAsync((window, model, _) =>
        {
            var previousLanguage = Lang.Current.Language;
            var assembly = typeof(ImageAnalyzer).Assembly;
            var needsRegistration = !Lang.Current.Keys.Contains("lang.kitopiaex.extract_text");
            try
            {
                if (needsRegistration) Lang.Current.RegisterAssembly(assembly);
                Lang.Current.UseLanguage("zh-CN");
                var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
                Application.Current!.RequestedThemeVariant = theme;
                window.RequestedThemeVariant = theme;
                using var image = new Mat(1, 1, MatType.CV_8UC4);
                var analyzer = new ImageAnalyzer();
                foreach (var item in analyzer.AnalyzeInputData([new InputData { InputType = InputType.图像, Data = image }]))
                    model.PinnedItems.Add(item);
                var shortcuts = model.PinnedItems.ToArray();
                model.PinnedItems.Add(new SearchViewItem { ItemDisplayName = "My 文档", IconSymbol = 0xF6EC });
                model.ShowPinnedItems = true;
                var types = new[] { FileType.数学运算, FileType.PDF文档, FileType.Word文档, FileType.图像 };
                var names = new[] { "=103", "English document.pdf", "我的文档.docx", "Photo.png" };
                for (var i = 0; i < types.Length; i++)
                    model.Items.Add(new SearchViewItem { ItemDisplayName = names[i], FileType = types[i], IconSymbol = 0xF6EC });
                Dispatcher.UIThread.RunJobs();
                var buttons = window.FindControl<ListBox>("PinnedList")!.GetVisualDescendants()
                    .OfType<Button>().Where(button => button.Name == "PinnedItem").ToArray();
                var labels = buttons.Select(button => button.GetVisualDescendants().OfType<TextBlock>()
                    .Single(label => label.FontSize == 12)).ToArray();
                var subtitles = window.FindControl<ListBox>("dataGrid")!.GetVisualDescendants()
                    .OfType<SearchItemSubtitle>().ToArray();
                Assert.HasCount(5, labels);
                Assert.HasCount(4, subtitles);
                foreach (var language in new[] { "zh-CN", "en-US", "zh-CN" })
                {
                    Lang.Current.UseLanguage(language);
                    Dispatcher.UIThread.RunJobs();
                    for (var i = 0; i < shortcuts.Length; i++)
                    {
                        Assert.AreSame(shortcuts[i], model.PinnedItems[i]);
                        Assert.AreEqual(Lang.Get(shortcuts[i].ItemDisplayName), labels[i].Text);
                        Assert.AreEqual(labels[i].Text, ToolTip.GetTip(buttons[i]));
                        Assert.IsNotNull(shortcuts[i].Action);
                    }
                    Assert.AreEqual(language == "en-US" ? "Save image" : "保存图片", labels[0].Text);
                    Assert.AreEqual("My 文档", labels[4].Text);
                    Assert.AreEqual(language == "en-US" ? "Calculations" : "数学运算", subtitles[0].Text);
                    for (var i = 0; i < types.Length; i++) Assert.AreEqual(Lang.Get(types[i]), subtitles[i].Text);
                    CollectionAssert.AreEqual(names, model.Items.Select(item => item.ItemDisplayName).ToArray());
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var frame = window.CaptureRenderedFrame();
                    Assert.IsNotNull(frame);
                    var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/search-keyboard"));
                    Directory.CreateDirectory(directory);
                    var path = Path.Combine(directory, $"search-labels-{themeName}-{language}.png");
                    frame.Save(path, PngBitmapEncoderOptions.Default);
                    if (!screenshots.Contains(path)) screenshots.Add(path);
                }
            }
            finally
            {
                if (needsRegistration) Lang.Current.UnregisterAssembly(assembly.GetName().Name!);
                Lang.Current.UseLanguage(previousLanguage);
            }
            return Task.CompletedTask;
        });
        foreach (var screenshot in screenshots) TestContext.AddResultFile(screenshot);
    }

    [TestMethod]
    public async Task SearchItemSubtitle_RecyclingAndLanguageChanges_PreservesUsageAndReleasesSubscriptions()
    {
        await RunAsync((_, _, _) =>
        {
            var previousLanguage = Lang.Current.Language;
            var previousServices = ServiceManager.Services;
            var usage = new SubtitleUsageService();
            using var services = new ServiceCollection().AddSingleton<IProcessUsageService>(usage).BuildServiceProvider();
            ServiceManager.Services = services;
            ConfigManger.Config!.showProcessUsageInSearch = true;
            ConfigManger.Config.showProcessCpuUsage = true;
            ConfigManger.Config.showProcessMemoryUsage = true;
            var item = new SearchViewItem { ItemDisplayName = "My app", OnlyKey = @"C:\sample.exe", FileType = FileType.应用程序 };
            var label = new SearchItemSubtitle { DataContext = item };
            var host = new Window { Content = label };
            try
            {
                Lang.Current.UseLanguage("en-US");
                host.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(1, usage.SubscriberCount);
                Assert.AreEqual("Applications · CPU 12.5% · Memory 64 MB", label.Text);
                Lang.Current.UseLanguage("zh-CN");
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual("应用程序 · CPU 12.5% · 内存 64 MB", label.Text);
                label.DataContext = new SearchViewItem { ItemDisplayName = "Document.pdf", FileType = FileType.PDF文档 };
                Assert.AreEqual("PDF 文档", label.Text);
                label.DataContext = null;
                Assert.AreEqual(string.Empty, label.Text);
                label.DataContext = item;
                Assert.AreEqual(1, usage.SubscriberCount);
                host.Content = null;
                Assert.AreEqual(0, usage.SubscriberCount);
                Lang.Current.UseLanguage("en-US");
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual("应用程序 · CPU 12.5% · 内存 64 MB", label.Text);
                host.Content = label;
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(1, usage.SubscriberCount);
                Assert.AreEqual("Applications · CPU 12.5% · Memory 64 MB", label.Text);
            }
            finally
            {
                host.Close();
                ServiceManager.Services = previousServices;
                Lang.Current.UseLanguage(previousLanguage);
                Assert.AreEqual(0, usage.SubscriberCount);
            }
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task ClipboardImage_InputChanges_KeepsSaveShortcutVisibleUntilImageIsRemoved()
    {
        await RunAsync((window, model, _) =>
        {
            using var image = new Mat(1, 1, MatType.CV_8UC4);
            var hasImage = true;
            PluginOverall.SearchWindowInputDataIdentifies["Kitopia"] =
            [
                (_, _) => hasImage ? [new InputData { InputType = InputType.图像, Data = image }] : []
            ];
            var analyzer = new ImageAnalyzer();
            PluginOverall.SearchWindowInputDataAnalyzers["KitopiaEx"] =
            [
                (() => analyzer.AnalyzeTimeFlags, inputs => analyzer.AnalyzeInputData(inputs))
            ];

            foreach (var flags in new[]
                     {
                         InputDataAnalyzeTimeFlags.WindowShow, InputDataAnalyzeTimeFlags.InputEmpty,
                         InputDataAnalyzeTimeFlags.InputChanged, InputDataAnalyzeTimeFlags.InputChanged
                     })
            {
                model.ProcessInputData(flags == InputDataAnalyzeTimeFlags.InputChanged ? "query" : null, flags);
                Dispatcher.UIThread.RunJobs();

                Assert.IsTrue(model.InputDatas.Any(input => input.InputType == InputType.图像));
                Assert.HasCount(4, model.PinnedItems);
                Assert.IsNotNull(model.PinnedItems.Single(item => item.IconSymbol == 0xE357).Action);
                Assert.IsTrue(window.FindControl<ListBox>("PinnedList")!.IsEffectivelyVisible);
            }

            hasImage = false;
            model.ProcessInputData("query", InputDataAnalyzeTimeFlags.InputChanged);
            Assert.IsEmpty(model.PinnedItems);
            Assert.IsFalse(model.ShowPinnedItems);

            hasImage = true;
            PluginOverall.SearchWindowInputDataAnalyzers.TryRemove("KitopiaEx", out var _);
            model.ProcessInputData("query", InputDataAnalyzeTimeFlags.InputChanged);
            Assert.IsTrue(model.ShowInputData);
            Assert.IsEmpty(model.PinnedItems);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task Search_LaptopWithManyResults_ScrollsWithinScreenLimit()
    {
        await RunAsync((window, model, _) =>
        {
            var screen = window.Screens.Primary!;
            var originalScaling = screen.Scaling;
            var originalArea = screen.WorkingArea;
            var scalingProperty = typeof(Screen).GetProperty(nameof(Screen.Scaling))!;
            var areaProperty = typeof(Screen).GetProperty(nameof(Screen.WorkingArea))!;
            const double scaling = 1.5;
            try
            {
                window.Hide();
                scalingProperty.SetValue(screen, scaling);
                areaProperty.SetValue(screen, new PixelRect(0, 0, 1920, 1040));
                window.SetRenderScaling(scaling);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                for (var i = 0; i < 60; i++)
                    model.Items.Add(new SearchViewItem { ItemDisplayName = $"搜索结果 {i + 1}", IconSymbol = 0xF6EC });
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(SizeToContent.Height, window.SizeToContent);
                Assert.IsTrue(window.Bounds.Height * scaling <= 1040 * 0.6 + 1);
                var results = window.FindControl<ListBox>("dataGrid")!;
                var scroll = results.GetVisualDescendants().OfType<ScrollViewer>().First();
                Assert.IsTrue(scroll.Extent.Height > scroll.Viewport.Height);
                window.FindControl<TextBox>("tx")!.Focus();
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                window.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.End, null);
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(59, results.SelectedIndex);
                Assert.IsTrue(scroll.Offset.Y > 0, "Results beyond the height limit must remain reachable.");

                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = window.CaptureRenderedFrame();
                Assert.IsNotNull(frame);
                var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/search-keyboard"));
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "search-laptop-150-percent.png");
                frame.Save(path, PngBitmapEncoderOptions.Default);
                TestContext.AddResultFile(path);
            }
            finally
            {
                scalingProperty.SetValue(screen, originalScaling);
                areaProperty.SetValue(screen, originalArea);
                window.SetRenderScaling(originalScaling);
            }
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task ShortcutNavigation_Overflow_ScrollsSelectionIntoViewAndOpensMenu()
    {
        var screenshots = new List<string>();
        await RunAsync((window, model, _) =>
        {
            var input = window.FindControl<TextBox>("tx")!;
            var shortcuts = window.FindControl<ListBox>("PinnedList")!;
            for (var i = 0; i < 14; i++)
                model.PinnedItems.Add(new SearchViewItem { ItemDisplayName = $"Shortcut with a long name {i}", IconSymbol = 0xF6EC });
            model.ShowPinnedItems = true;
            Dispatcher.UIThread.RunJobs();
            input.Focus();
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            for (var i = 1; i < model.PinnedItems.Count; i++)
                window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(13, shortcuts.SelectedIndex);
            var scroll = shortcuts.GetVisualDescendants().OfType<ScrollViewer>().First();
            Assert.IsGreaterThan(0, scroll.Offset.X);
            var container = shortcuts.ContainerFromIndex(13)!;
            var bounds = container.Bounds;
            var position = container.TranslatePoint(default, shortcuts)!.Value;
            Assert.IsGreaterThanOrEqualTo(0, position.X);
            Assert.IsLessThanOrEqualTo(shortcuts.Bounds.Width, position.X + bounds.Width);

            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Application.Current!.RequestedThemeVariant = theme;
                window.RequestedThemeVariant = theme;
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = window.CaptureRenderedFrame();
                Assert.IsNotNull(frame);
                var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/search-keyboard"));
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, $"search-shortcuts-{theme}.png");
                frame.Save(path, PngBitmapEncoderOptions.Default);
                screenshots.Add(path);
            }

            window.KeyPress(Key.F10, RawInputModifiers.Shift, PhysicalKey.F10, null);
            var button = container.GetVisualDescendants().OfType<Button>().First();
            Assert.IsTrue(button.ContextFlyout!.IsOpen);
            button.ContextFlyout.Hide();
            return Task.CompletedTask;
        });
        foreach (var screenshot in screenshots) TestContext.AddResultFile(screenshot);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EmptySearchLayout_HistoryAndSuggestions_UsesConfiguredLayoutAndSelection(bool vertical)
    {
        Assert.IsFalse(new KitopiaConfig().verticalEmptySearchResults);
        await RunAsync(async (window, model, services) =>
        {
            var config = ConfigManger.Config!;
            var index = (SearchServicesProxy)ServiceManager.Services.GetRequiredService<IIndexService>();
            var paths = new[] { @"C:\Apps\Pinned.exe", @"C:\Documents\Recent.pdf", @"C:\Documents\Older.docx" };
            index.Results = paths.Select((path, i) => new SearchIndexResult(new SearchEntry
            {
                OnlyKey = path, DisplayName = $"History {i}", FileType = FileType.文件, IconSymbol = 0xF6EC
            }, 1, null)).ToArray();
            config.alwayShows.Add(paths[0]);
            config.maxHistory = 3;
            for (var i = 0; i < paths.Length; i++)
                config.lastOpens[paths[i]] = new HistoryItem { AccessTimes = [DateTime.Now.AddDays(-i)] };
            var mini = new SearchViewItem
            {
                ItemDisplayName = "Clipboard shortcut", OnlyKey = "mini-action", ShowAsMiniApp = true, IconSymbol = 0xF6EC
            };
            var suggestion = new SearchViewItem
            {
                ItemDisplayName = "Clipboard suggestion", OnlyKey = "row-action", IconSymbol = 0xF6EC
            };
            PluginOverall.SearchWindowInputDataAnalyzers["Kitopia"] =
            [
                (() => InputDataAnalyzeTimeFlags.InputEmpty, _ => [suggestion, mini])
            ];

            model.LoadLast();
            Dispatcher.UIThread.RunJobs();
            var input = window.FindControl<TextBox>("tx")!;
            var results = window.FindControl<ListBox>("dataGrid")!;
            var shortcuts = window.FindControl<ListBox>("PinnedList")!;
            CollectionAssert.AreEqual(vertical ? new[] { "mini-action", "row-action", paths[0], paths[1], paths[2] } : new[] { "row-action" },
                model.Items.Select(item => item.OnlyKey).ToArray());
            CollectionAssert.AreEqual(vertical ? Array.Empty<string>() : new[] { "mini-action", paths[0], paths[1], paths[2] },
                model.PinnedItems.Select(item => item.OnlyKey).ToArray());
            Assert.AreEqual(!vertical, shortcuts.IsEffectivelyVisible);
            Assert.IsTrue(input.IsFocused);
            Assert.AreSame(mini, model.SelectedPinnedItem ?? model.SelectedItem);
            Assert.AreEqual(vertical ? 0 : -1, results.SelectedIndex);
            Assert.AreEqual(vertical ? -1 : 0, shortcuts.SelectedIndex);

            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            var list = vertical ? results : shortcuts;
            Assert.AreEqual(1, list.SelectedIndex);
            Assert.IsTrue(list.IsKeyboardFocusWithin);
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            Assert.AreEqual(0, list.SelectedIndex);
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            Assert.IsTrue(input.IsFocused);
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            var selected = (SearchViewItem)list.SelectedItem!;
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.AreSame(selected, await services.Opened.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        }, initiallyVerticalEmptyResults: vertical);
    }

    [TestMethod]
    public async Task EmptySearchLayout_SettingQueryAndPinChanges_KeepsOneVisibleCopy()
    {
        await RunAsync(async (window, model, _) =>
        {
            var config = ConfigManger.Config!;
            var index = (SearchServicesProxy)ServiceManager.Services.GetRequiredService<IIndexService>();
            const string path = @"C:\Documents\History.pdf";
            index.Results = [new SearchIndexResult(new SearchEntry
            {
                OnlyKey = path, DisplayName = "History document", FileType = FileType.文件, IconSymbol = 0xF6EC
            }, 1, null)];
            config.lastOpens[path] = new HistoryItem { AccessTimes = [DateTime.Now] };
            var mini = new SearchViewItem { ItemDisplayName = "Clipboard shortcut", ShowAsMiniApp = true, IconSymbol = 0xF6EC };
            PluginOverall.SearchWindowInputDataAnalyzers["Kitopia"] =
            [
                (() => InputDataAnalyzeTimeFlags.InputEmpty | InputDataAnalyzeTimeFlags.InputChanged, _ => [mini])
            ];
            model.LoadLast();
            Dispatcher.UIThread.RunJobs();
            var shortcuts = window.FindControl<ListBox>("PinnedList")!;
            Assert.HasCount(2, model.PinnedItems);
            Assert.IsEmpty(model.Items);

            config.verticalEmptySearchResults = true;
            config.OnConfigChanged(this, nameof(KitopiaConfig.verticalEmptySearchResults), true);
            Dispatcher.UIThread.RunJobs();
            Assert.IsEmpty(model.PinnedItems);
            Assert.IsFalse(shortcuts.IsEffectivelyVisible);
            Assert.HasCount(2, model.Items);

            foreach (var pinned in new[] { true, false })
            {
                model.PinCommand.Execute(model.Items.Single(item => item.OnlyKey == path));
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(pinned, config.alwayShows.Contains(path));
                Assert.AreEqual(pinned, model.Items.Single(item => item.OnlyKey == path).IsPined);
                Assert.HasCount(2, model.Items);
                Assert.IsEmpty(model.PinnedItems);
                Assert.IsFalse(shortcuts.IsEffectivelyVisible);
            }

            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            model.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(model.IsSemanticSearching) && !model.IsSemanticSearching)
                    completed.TrySetResult();
            };
            model.Search = "history";
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            CollectionAssert.AreEqual(new[] { mini }, model.PinnedItems.ToArray());
            Assert.IsTrue(shortcuts.IsEffectivelyVisible);
            Assert.AreEqual(path, model.Items.Single().OnlyKey);

            model.Search = string.Empty;
            Dispatcher.UIThread.RunJobs();
            Assert.IsEmpty(model.PinnedItems);
            Assert.IsFalse(shortcuts.IsEffectivelyVisible);
            Assert.HasCount(2, model.Items);
            Assert.AreSame(mini, model.SelectedItem);

            config.verticalEmptySearchResults = false;
            config.OnConfigChanged(this, nameof(KitopiaConfig.verticalEmptySearchResults), false);
            Dispatcher.UIThread.RunJobs();
            Assert.IsEmpty(model.Items);
            Assert.HasCount(2, model.PinnedItems);
            Assert.IsTrue(shortcuts.IsEffectivelyVisible);
            Assert.AreSame(mini, model.SelectedPinnedItem);
        });
    }

    [TestMethod]
    [DataRow("Light")]
    [DataRow("Dark")]
    public async Task EmptySearchLayout_OverflowAndLanguageChanges_ScrollsLocalizedRows(string themeName)
    {
        var screenshots = new List<string>();
        await RunAsync((window, model, _) =>
        {
            var previousLanguage = Lang.Current.Language;
            var assembly = typeof(ImageAnalyzer).Assembly;
            var needsRegistration = !Lang.Current.Keys.Contains("lang.kitopiaex.extract_text");
            try
            {
                if (needsRegistration) Lang.Current.RegisterAssembly(assembly);
                var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
                Application.Current!.RequestedThemeVariant = theme;
                window.RequestedThemeVariant = theme;
                var config = ConfigManger.Config!;
                var index = (SearchServicesProxy)ServiceManager.Services.GetRequiredService<IIndexService>();
                config.maxHistory = 30;
                index.Results = Enumerable.Range(0, 30).Select(i => new SearchIndexResult(new SearchEntry
                {
                    OnlyKey = $@"C:\Documents\Projects\A long directory name for search layout verification\History document {i}.pdf",
                    DisplayName = $"History document {i}.pdf", FileType = FileType.PDF文档, IconSymbol = 0xF6EC
                }, 1, null)).ToArray();
                for (var i = 0; i < index.Results.Count; i++)
                    config.lastOpens[index.Results[i].Source.OnlyKey] = new HistoryItem { AccessTimes = [DateTime.Now.AddDays(-i)] };
                using var image = new Mat(1, 1, MatType.CV_8UC4);
                PluginOverall.SearchWindowInputDataIdentifies["Kitopia"] =
                [
                    (_, _) => [new InputData { InputType = InputType.图像, Data = image }]
                ];
                var analyzer = new ImageAnalyzer();
                PluginOverall.SearchWindowInputDataAnalyzers["KitopiaEx"] =
                [
                    (() => analyzer.AnalyzeTimeFlags, inputs => analyzer.AnalyzeInputData(inputs))
                ];
                model.LoadLast();
                Dispatcher.UIThread.RunJobs();
                var results = window.FindControl<ListBox>("dataGrid")!;
                var scroll = results.GetVisualDescendants().OfType<ScrollViewer>().First();
                Assert.HasCount(34, model.Items);
                Assert.IsEmpty(model.PinnedItems);
                Assert.IsFalse(window.FindControl<ListBox>("PinnedList")!.IsEffectivelyVisible);
                Assert.IsTrue(scroll.Extent.Height > scroll.Viewport.Height);
                Assert.IsTrue(scroll.Extent.Width <= scroll.Viewport.Width + 1);
                var first = results.ContainerFromIndex(0)!;
                var second = results.ContainerFromIndex(1)!;
                Assert.IsTrue(second.Bounds.Y >= first.Bounds.Bottom);
                Assert.IsTrue(model.Items.Take(4).All(item => item.OnlyKey.Length == 0));
                for (var i = 0; i < 4; i++)
                {
                    var action = results.ContainerFromIndex(i)!;
                    Assert.IsFalse(action.GetVisualDescendants().OfType<PinButton>().Single().IsVisible);
                    model.PinCommand.Execute(model.Items[i]);
                }
                Assert.IsEmpty(config.alwayShows, "Inline actions without a stable key cannot be pinned.");
                Assert.IsTrue(results.ContainerFromIndex(4)!.GetVisualDescendants().OfType<PinButton>().Single().IsVisible);

                foreach (var language in new[] { "zh-CN", "en-US" })
                {
                    Lang.Current.UseLanguage(language);
                    Dispatcher.UIThread.RunJobs();
                    var title = first.GetVisualDescendants().OfType<TextBlock>()
                        .Single(label => label.Classes.Contains("search-item-title"));
                    Assert.AreEqual(Lang.Get(model.Items[0].ItemDisplayName),
                        string.Concat(title.Inlines!.OfType<Run>().Select(run => run.Text)));
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var frame = window.CaptureRenderedFrame();
                    Assert.IsNotNull(frame);
                    var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/search-keyboard"));
                    Directory.CreateDirectory(directory);
                    var screenshot = Path.Combine(directory, $"search-empty-vertical-{themeName}-{language}.png");
                    frame.Save(screenshot, PngBitmapEncoderOptions.Default);
                    screenshots.Add(screenshot);
                }

                window.FindControl<TextBox>("tx")!.Focus();
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                window.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.End, null);
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(model.Items.Count - 1, results.SelectedIndex);
                Assert.IsTrue(scroll.Offset.Y > 0);
                var last = results.ContainerFromIndex(results.SelectedIndex)!;
                var path = last.GetVisualDescendants().OfType<TextBlock>()
                    .Single(label => label.Classes.Contains("search-item-path"));
                Assert.AreEqual(model.Items[^1].OnlyKey, path.Text);
                Assert.IsTrue(path.Bounds.Width > 0);
            }
            finally
            {
                if (needsRegistration) Lang.Current.UnregisterAssembly(assembly.GetName().Name!);
                Lang.Current.UseLanguage(previousLanguage);
            }
            return Task.CompletedTask;
        }, initiallyVerticalEmptyResults: true);
        foreach (var screenshot in screenshots) TestContext.AddResultFile(screenshot);
    }

    private static async Task RunAsync(Func<SearchWindow, SearchWindowViewModel, SearchServicesProxy, Task> test,
        bool initiallyBorderless = false, bool initiallyVerticalEmptyResults = false)
    {
        ReactiveUI.Builder.RxAppBuilder.CreateReactiveUIBuilder().WithPlatformServices().BuildApp();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SearchWindowKeyboardTests));
        await session.Dispatch(async () =>
        {
            // Other headless sessions can leave recipients owned by a disposed UI dispatcher.
            WeakReferenceMessenger.Default.Reset();
            var originalConfigs = ConfigManger.Configs;
            var originalServices = ServiceManager.Services;
            var originalTheme = Application.Current!.RequestedThemeVariant;
            var identifiers = PluginOverall.SearchWindowInputDataIdentifies.ToArray();
            var analyzers = PluginOverall.SearchWindowInputDataAnalyzers.ToArray();
            ConfigManger.Configs = new Dictionary<string, ConfigBase>
            {
                ["KitopiaConfig"] = new KitopiaConfig
                {
                    useEverythingForIndexing = false, enableSemanticSearch = false, semanticSearchDebounceMilliseconds = 100,
                    hideSearchWindowBorder = initiallyBorderless, verticalEmptySearchResults = initiallyVerticalEmptyResults
                }
            };
            PluginOverall.SearchWindowInputDataIdentifies.Clear();
            PluginOverall.SearchWindowInputDataAnalyzers.Clear();
            PluginOverall.SearchWindowInputDataIdentifies["Kitopia"] = [];
            PluginOverall.SearchWindowInputDataAnalyzers["Kitopia"] = [];
            var itemTool = DispatchProxy.Create<ISearchItemTool, SearchServicesProxy>();
            using var services = new ServiceCollection()
                .AddSingleton(DispatchProxy.Create<IWindowTool, SearchServicesProxy>())
                .AddSingleton(DispatchProxy.Create<IAppToolService, SearchServicesProxy>())
                .AddSingleton(DispatchProxy.Create<IIndexService, SearchServicesProxy>())
                .AddSingleton(itemTool).BuildServiceProvider();
            ServiceManager.Services = services;
            var model = new SearchWindowViewModel();
            var window = new SearchWindow { DataContext = model };
            window.Closing -= typeof(SearchWindow).GetMethod("Window_OnClosing", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate<EventHandler<WindowClosingEventArgs>>(window);
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                await test(window, model, (SearchServicesProxy)itemTool);
            }
            finally
            {
                model.Search = string.Empty;
                Dispatcher.UIThread.RunJobs();
                window.Close();
                WeakReferenceMessenger.Default.UnregisterAll(window);
                model.FilePreview.Dispose();
                Application.Current!.RequestedThemeVariant = originalTheme;
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

    private sealed class SubtitleUsageService : IProcessUsageService
    {
        public bool Enabled { get; set; }
        public event Action? Updated;
        public int SubscriberCount => Updated?.GetInvocationList().Length ?? 0;
        public void SetTargets(IEnumerable<string> processNames) { }
        public bool TryGet(string processName, out ProcessUsageSnapshot snapshot)
        {
            snapshot = new ProcessUsageSnapshot(processName, 12.5, 64 * 1024 * 1024);
            return true;
        }
    }

    private class SearchServicesProxy : DispatchProxy
    {
        public TaskCompletionSource<SearchViewItem> Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<SearchIndexResult> Results { get; set; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == nameof(IIndexService.TryGetValue))
            {
                var entry = Results.Select(result => result.Source).FirstOrDefault(entry => entry.OnlyKey == (string)args![0]!);
                args![1] = entry;
                return entry.OnlyKey is not null;
            }
            if (targetMethod.Name == nameof(ISearchItemTool.Pin))
            {
                var item = (SearchViewItem)args![0]!;
                item.IsPined = !ConfigManger.Config!.alwayShows.Contains(item.OnlyKey);
                if (item.IsPined) ConfigManger.Config.alwayShows.Insert(0, item.OnlyKey);
                else ConfigManger.Config.alwayShows.Remove(item.OnlyKey);
                return null;
            }
            return targetMethod.Name switch
            {
                nameof(IWindowTool.MoveWindowToMouseScreenCenter) or nameof(IWindowTool.SetForegroundWindow)
                    or nameof(IAppToolService.LoadIcon) => null,
                nameof(IIndexService.SearchPinyin) => Results,
                nameof(IIndexService.SearchAsync) => Task.FromResult(Results),
                nameof(ISearchItemTool.OpenFile) => Opened.TrySetResult((SearchViewItem)args![0]!),
                _ => throw new NotSupportedException(targetMethod.Name)
            };
        }
    }
}
