using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Messaging;
using Kitopia.Desktop.Features.Indexing;
using Kitopia.Desktop.Features.Search;
using Kitopia.Desktop.Features.Search.ViewModels;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Plugin;
using Kitopia.Desktop.Services;
using Kitopia.Desktop.Windows;
using KitopiaEx.SearchWindow.InputDataAnalyzer;
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
    public async Task Search_ResultsArrive_SelectsFirstResultOnlyInPreviewMode(FileType fileType, bool preview)
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

                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Assert.AreSame(expected, await services.Opened.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            }
            else
            {
                Assert.AreEqual(-1, results.SelectedIndex);
                Assert.AreEqual(-1, shortcuts.SelectedIndex);
                Assert.IsNull(model.SelectedItem);
                Assert.AreEqual("", model.FilePreview.SelectedPath);

                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Assert.IsTrue(window.IsVisible);
                Assert.IsNull(model.SelectedItem);
                Assert.IsFalse(services.Opened.Task.IsCompleted);
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                Assert.AreEqual(0, results.SelectedIndex);
                window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
                Dispatcher.UIThread.RunJobs();
                Assert.IsTrue(input.IsFocused);
                Assert.AreEqual(-1, results.SelectedIndex);
                Assert.IsNull(model.SelectedItem);
                Assert.AreEqual("", model.FilePreview.SelectedPath);

                model.Items.Add(new SearchViewItem { ItemDisplayName = "Later result", IconSymbol = 0xF6EC });
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(-1, results.SelectedIndex);
                Assert.IsNull(model.SelectedItem);
            }
        });
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task InputRegainsFocus_ShortcutOrResultSelected_ClearsSelectionAndDoesNotOpenOnEnter(bool fromShortcuts)
    {
        await RunAsync((window, model, services) =>
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
            Assert.AreEqual(-1, results.SelectedIndex);
            Assert.AreEqual(-1, shortcuts.SelectedIndex);
            Assert.IsNull(model.SelectedItem);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.IsTrue(window.IsVisible);
            Assert.IsFalse(services.Opened.Task.IsCompleted);
            return Task.CompletedTask;
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
            Assert.AreEqual(-1, shortcuts.SelectedIndex);
            Assert.AreEqual(-1, results.SelectedIndex);
            Assert.IsNull(model.SelectedItem);
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
            Assert.AreEqual(-1, results.SelectedIndex);
            Assert.IsNull(model.SelectedItem);
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

    private static async Task RunAsync(Func<SearchWindow, SearchWindowViewModel, SearchServicesProxy, Task> test)
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
                    useEverything = false, enableSemanticSearch = false, semanticSearchDebounceMilliseconds = 100
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

    private class SearchServicesProxy : DispatchProxy
    {
        public TaskCompletionSource<SearchViewItem> Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<SearchIndexResult> Results { get; set; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod!.Name switch
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
