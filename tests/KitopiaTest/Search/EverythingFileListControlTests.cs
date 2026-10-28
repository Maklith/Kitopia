using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.Raw;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Messaging;
using Kitopia.Desktop.Controls;
using Kitopia.Desktop.Features.Indexing;
using Kitopia.Desktop.Features.Search;
using Kitopia.Desktop.Features.Search.ViewModels;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Plugin;
using Kitopia.Desktop.Services;
using Kitopia.Desktop.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ObservableCollections;
using PluginCore;
using PluginCore.Config;
using PluginCore.SearchWindow.InputData;
using PluginCore.SearchWindow.InputDataAnalyzer;

namespace KitopiaTest.Search;

[TestClass]
[DoNotParallelize]
public sealed class EverythingFileListControlTests
{
    public TestContext TestContext { get; set; } = null!;

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Kitopia.Desktop.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia();

    [TestMethod]
    public void SearchViewItem_FileSizeAndDateModified_Formatting()
    {
        var fileItem = new SearchViewItem
        {
            ItemDisplayName = "document.pdf",
            StartDirectory = "C:\\Docs",
            FileSize = 1048576,
            DateModified = new DateTime(2026, 10, 27, 14, 30, 0),
            IsFolder = false
        };

        Assert.AreEqual("1.0 MB", fileItem.FormattedFileSize);
        Assert.AreEqual("2026/10/27 14:30", fileItem.FormattedDateModified);

        var folderItem = new SearchViewItem
        {
            ItemDisplayName = "MyFolder",
            StartDirectory = "C:\\",
            FileSize = 2048,
            DateModified = new DateTime(2026, 1, 5, 9, 5, 0),
            IsFolder = true
        };

        Assert.AreEqual(string.Empty, folderItem.FormattedFileSize);
        Assert.AreEqual("2026/1/5 09:05", folderItem.FormattedDateModified);

        var nullItem = new SearchViewItem
        {
            ItemDisplayName = "Unknown",
            FileSize = null,
            DateModified = null
        };

        Assert.AreEqual(string.Empty, nullItem.FormattedFileSize);
        Assert.AreEqual(string.Empty, nullItem.FormattedDateModified);
    }

    [TestMethod]
    public void FormatBytes_BoundaryValues()
    {
        Assert.AreEqual("0 B", SearchViewItem.FormatBytes(0));
        Assert.AreEqual("500 B", SearchViewItem.FormatBytes(500));
        Assert.AreEqual("1.0 KB", SearchViewItem.FormatBytes(1024));
        Assert.AreEqual("1.5 KB", SearchViewItem.FormatBytes(1536));
        Assert.AreEqual("1.00 GB", SearchViewItem.FormatBytes(1073741824));
        Assert.AreEqual("1.00 TB", SearchViewItem.FormatBytes(1099511627776));
    }

    [TestMethod]
    public async Task SearchWindow_EverythingMode_PreviewableResults_RequireManualPreviewAcrossPagesAndSortChanges()
    {
        await RunAsync(async (_, model, _) =>
        {
            SearchServicesProxy.PagedEverythingResults = (_, offset, count) =>
                Enumerable.Range(offset, count).Select(i => new SearchViewItem
                {
                    ItemDisplayName = $"document_{i}.pdf",
                    OnlyKey = $"C:\\Files\\document_{i}.pdf",
                    FileType = FileType.PDF文档
                }).ToList();
            model.Search = "@document";
            await WaitForAsync(() => model.Items.Count == 100 && model.LoadMoreEverythingCommand.CanExecute(null));
            Assert.IsTrue(model.CanUsePreview);
            Assert.IsFalse(model.IsPreviewMode);
            Assert.AreEqual(string.Empty, model.FilePreview.SelectedPath);

            await model.LoadMoreEverythingAsync();
            Assert.IsFalse(model.IsPreviewMode);

            model.TogglePreviewModeCommand.Execute(null);
            Assert.IsTrue(model.IsPreviewMode);
            await model.LoadMoreEverythingAsync();
            Assert.IsTrue(model.IsPreviewMode);

            model.EverythingSortOrder = EverythingSortOrder.SizeDescending;
            await WaitForAsync(() => model.Items.Count == 100 && model.LoadMoreEverythingCommand.CanExecute(null));
            Assert.IsFalse(model.IsPreviewMode);

            model.TogglePreviewModeCommand.Execute(null);
            Assert.IsTrue(model.IsPreviewMode);
            model.Search = "@another";
            await WaitForAsync(() => model.Items.Count == 100 && model.LoadMoreEverythingCommand.CanExecute(null));
            Assert.IsFalse(model.IsPreviewMode);
        });
    }

    [TestMethod]
    public async Task SearchWindow_PreviewSplitter_DragResizesBothPanesAndToggleRestoresListWidth()
    {
        await RunAsync((window, model, _) =>
        {
            model.IsPreviewMode = true;
            Dispatcher.UIThread.RunJobs();
            var splitter = window.FindControl<GridSplitter>("PreviewSplitter")!;
            var grid = (Grid)splitter.Parent!;
            var leftWidth = grid.ColumnDefinitions[0].ActualWidth;
            var rightWidth = grid.ColumnDefinitions[2].ActualWidth;
            Assert.IsTrue(splitter.Bounds.Width >= 6);
            using var frame = window.CaptureRenderedFrame();
            var start = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), window)!.Value;
            Assert.IsTrue(ReferenceEquals(window.InputHitTest(start), splitter) ||
                          (window.InputHitTest(start) as Visual)?.FindAncestorOfType<GridSplitter>() == splitter,
                $"Splitter hit test: {window.InputHitTest(start)}, bounds: {splitter.Bounds}, point: {start}");
            var started = false;
            splitter.DragStarted += (_, _) => started = true;
            window.MouseDown(start, MouseButton.Left);
            Assert.IsTrue(started, "The splitter should receive pointer presses.");
            window.MouseMove(start + new Vector(100, 0));
            window.MouseUp(start + new Vector(100, 0), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(leftWidth + 100, grid.ColumnDefinitions[0].ActualWidth, 2);
            Assert.AreEqual(rightWidth - 100, grid.ColumnDefinitions[2].ActualWidth, 2);

            model.IsPreviewMode = false;
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(0, grid.ColumnDefinitions[1].ActualWidth);
            Assert.AreEqual(0, grid.ColumnDefinitions[2].ActualWidth);
            Assert.AreEqual(grid.Bounds.Width, grid.ColumnDefinitions[0].ActualWidth, 2);

            model.IsPreviewMode = true;
            Dispatcher.UIThread.RunJobs();
            Assert.IsTrue(grid.ColumnDefinitions[2].ActualWidth > 0);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task SearchWindow_EverythingMode_SwitchesControlVisibility()
    {
        await RunAsync((window, model, _) =>
        {
            var dataGrid = window.FindControl<ListBox>("dataGrid")!;
            var everythingControl = window.FindControl<EverythingFileListControl>("everythingControl")!;

            Assert.IsNotNull(dataGrid);
            Assert.IsNotNull(everythingControl);

            // Default state: not everything mode
            Assert.IsFalse(model.IsEverythingMode);
            Assert.IsTrue(dataGrid.IsVisible);
            Assert.IsFalse(everythingControl.IsVisible);

            // Typing @ triggers Everything mode
            model.Search = "@test";
            Dispatcher.UIThread.RunJobs();

            Assert.IsTrue(model.IsEverythingMode);
            Assert.IsFalse(dataGrid.IsVisible);
            Assert.IsTrue(everythingControl.IsVisible);

            // Clearing @ restores regular search
            model.Search = "test";
            Dispatcher.UIThread.RunJobs();

            Assert.IsFalse(model.IsEverythingMode);
            Assert.IsTrue(dataGrid.IsVisible);
            Assert.IsFalse(everythingControl.IsVisible);

            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task SearchWindow_EverythingMode_KeyboardNavigation()
    {
        await RunAsync(async (window, model, itemTool) =>
        {
            var input = window.FindControl<TextBox>("tx")!;
            var everythingControl = window.FindControl<EverythingFileListControl>("everythingControl")!;
            var internalGrid = everythingControl.FindControl<DataGrid>("FileListGrid")!;

            var item1 = new SearchViewItem
            {
                ItemDisplayName = "report.docx",
                StartDirectory = "D:\\Reports",
                FileSize = 51200,
                DateModified = new DateTime(2026, 10, 20, 10, 0, 0),
                OnlyKey = "D:\\Reports\\report.docx"
            };
            var item2 = new SearchViewItem
            {
                ItemDisplayName = "summary.xlsx",
                StartDirectory = "D:\\Reports",
                FileSize = 102400,
                DateModified = new DateTime(2026, 10, 25, 11, 0, 0),
                OnlyKey = "D:\\Reports\\summary.xlsx"
            };

            SearchServicesProxy.EverythingResults = [item1, item2];
            model.Search = "@report";
            Dispatcher.UIThread.RunJobs();

            input.Focus();
            Assert.IsTrue(input.IsFocused);
            Assert.IsNull(model.SelectedItem);

            // Press Down arrow to navigate into everythingControl
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual(item1, model.SelectedItem);
            Assert.AreEqual(0, internalGrid.SelectedIndex);

            // Press Enter activates item1
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();

            Assert.AreSame(item1, await itemTool.Opened.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        });
    }

    [TestMethod]
    public async Task SearchWindow_EverythingMode_NavigateUpReturnsFocusToInput()
    {
        await RunAsync((window, model, _) =>
        {
            var input = window.FindControl<TextBox>("tx")!;
            var everythingControl = window.FindControl<EverythingFileListControl>("everythingControl")!;
            var internalGrid = everythingControl.FindControl<DataGrid>("FileListGrid")!;

            var item1 = new SearchViewItem
            {
                ItemDisplayName = "code.cs",
                StartDirectory = "C:\\Projects",
                FileSize = 4096,
                DateModified = new DateTime(2026, 9, 1, 8, 0, 0),
                OnlyKey = "C:\\Projects\\code.cs"
            };
            SearchServicesProxy.EverythingResults = [item1];
            model.Search = "@code";
            Dispatcher.UIThread.RunJobs();

            // Select first item and focus grid
            everythingControl.FocusFirstItem();
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual(0, internalGrid.SelectedIndex);

            // Up arrow from first item returns focus to input
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            Dispatcher.UIThread.RunJobs();

            Assert.IsTrue(input.IsFocused);

            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task SearchWindow_EverythingMode_Loads100Initially_AndLoadMoreAppendsNextBatch()
    {
        await RunAsync(async (window, model, _) =>
        {
            SearchServicesProxy.EverythingCalls.Clear();
            SearchServicesProxy.PagedEverythingResults = (kw, offset, count) =>
            {
                return Enumerable.Range(offset, count).Select(i => new SearchViewItem
                {
                    ItemDisplayName = $"file_{i}.txt",
                    StartDirectory = "C:\\Files",
                    OnlyKey = $"C:\\Files\\file_{i}.txt",
                    FileSize = 1024,
                    DateModified = DateTime.Now
                }).ToList();
            };

            model.Search = "@sample";

            // Allow background task to execute
            for (var i = 0; i < 50 && model.Items.Count == 0; i++)
            {
                await Task.Delay(20);
                Dispatcher.UIThread.RunJobs();
            }

            Assert.AreEqual(100, model.Items.Count);
            Assert.AreEqual(1, SearchServicesProxy.EverythingCalls.Count);
            Assert.AreEqual(0, SearchServicesProxy.EverythingCalls[0].Offset);
            Assert.AreEqual(100, SearchServicesProxy.EverythingCalls[0].Count);

            // CanLoadMoreEverything should be true since first batch had 100 items
            Assert.IsTrue(model.LoadMoreEverythingCommand.CanExecute(null));

            // Load next batch via command
            await model.LoadMoreEverythingCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual(200, model.Items.Count);
            Assert.AreEqual(2, SearchServicesProxy.EverythingCalls.Count);
            Assert.AreEqual(100, SearchServicesProxy.EverythingCalls[1].Offset);
            Assert.AreEqual(100, SearchServicesProxy.EverythingCalls[1].Count);
        });
    }

    [TestMethod]
    public async Task SearchWindow_EverythingMode_ScrollToBottom_LoadsNextPageAndStopsAtEnd()
    {
        await RunAsync(async (window, model, _) =>
        {
            SearchServicesProxy.PagedEverythingResults = (_, offset, count) =>
                Enumerable.Range(offset, Math.Min(count, 150 - offset)).Select(i => new SearchViewItem
                {
                    ItemDisplayName = $"file_{i}.txt",
                    OnlyKey = $"C:\\Files\\file_{i}.txt",
                    StartDirectory = "C:\\Files"
                }).ToList();
            model.Search = "@sample";
            await WaitForAsync(() => model.Items.Count == 100 && model.LoadMoreEverythingCommand.CanExecute(null));

            var grid = window.FindControl<EverythingFileListControl>("everythingControl")!
                .FindControl<DataGrid>("FileListGrid")!;
            var scrollBar = grid.GetVisualDescendants().OfType<ScrollBar>()
                .Single(bar => bar.Name == "PART_VerticalScrollbar");
            Assert.IsTrue(scrollBar.Maximum > 0);
            grid.ScrollIntoView(model.Items.Last(), null);
            Dispatcher.UIThread.RunJobs();

            await WaitForAsync(() => model.Items.Count == 150 && !model.LoadMoreEverythingCommand.IsRunning);
            Assert.AreEqual(2, SearchServicesProxy.EverythingCalls.Count);
            Assert.AreEqual(100, SearchServicesProxy.EverythingCalls[1].Offset);
            Assert.IsFalse(model.LoadMoreEverythingCommand.CanExecute(null));

            grid.ScrollIntoView(model.Items.Last(), null);
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(2, SearchServicesProxy.EverythingCalls.Count);
        });
    }

    [TestMethod]
    public async Task SearchWindow_EverythingMode_SortColumns_RequeriesFromFirstPageAndKeepsServerOrder()
    {
        await RunAsync(async (window, model, _) =>
        {
            SearchServicesProxy.PagedEverythingResults = (_, offset, count) =>
                Enumerable.Range(offset, count).Reverse().Select(i => new SearchViewItem
                {
                    ItemDisplayName = $"file_{i:D3}.txt",
                    OnlyKey = $"C:\\Files\\file_{i}.txt",
                    StartDirectory = "C:\\Files",
                    FileSize = i,
                    DateModified = new DateTime(2026, 1, 1).AddMinutes(i)
                }).ToList();
            model.Search = "@sample";
            await WaitForAsync(() => model.Items.Count == 100 && model.LoadMoreEverythingCommand.CanExecute(null));
            await model.LoadMoreEverythingAsync();
            Assert.AreEqual(200, model.Items.Count);

            var grid = window.FindControl<EverythingFileListControl>("everythingControl")!
                .FindControl<DataGrid>("FileListGrid")!;
            var orders = new[]
            {
                EverythingSortOrder.NameDescending,
                EverythingSortOrder.PathAscending,
                EverythingSortOrder.SizeAscending,
                EverythingSortOrder.DateModifiedAscending
            };
            for (var column = 0; column < grid.Columns.Count; column++)
            {
                var calls = SearchServicesProxy.EverythingCalls.Count;
                grid.Columns[column].Sort();
                Dispatcher.UIThread.RunJobs();
                await WaitForAsync(() => SearchServicesProxy.EverythingCalls.Count > calls &&
                                         model.Items.Count == 100 && model.LoadMoreEverythingCommand.CanExecute(null));
                var request = SearchServicesProxy.EverythingCalls.Last();
                Assert.AreEqual(0, request.Offset);
                Assert.AreEqual(orders[column], request.SortOrder);
                Assert.AreEqual("sample", request.Keyword);
                CollectionAssert.AreEqual(model.Items.ToArray(), grid.CollectionView.Cast<SearchViewItem>().ToArray());

                await model.LoadMoreEverythingAsync();
                Assert.AreEqual(100, SearchServicesProxy.EverythingCalls.Last().Offset);
                Assert.AreEqual(orders[column], SearchServicesProxy.EverythingCalls.Last().SortOrder);
                Assert.AreEqual(200, model.Items.Count);
                CollectionAssert.AreEqual(model.Items.ToArray(), grid.CollectionView.Cast<SearchViewItem>().ToArray());
            }

            model.Search = "@another";
            await WaitForAsync(() => model.Items.Count == 100 && model.LoadMoreEverythingCommand.CanExecute(null));
            Assert.AreEqual(EverythingSortOrder.DateModifiedAscending, SearchServicesProxy.EverythingCalls.Last().SortOrder);
        });
    }

    [TestMethod]
    public async Task SearchWindow_EverythingMode_SortDuringPageLoad_DiscardsOldPageAndPreservesLoadingState()
    {
        await RunAsync(async (_, model, _) =>
        {
            var pageStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var sortedStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var releasePage = new ManualResetEventSlim();
            using var releaseSorted = new ManualResetEventSlim();
            SearchServicesProxy.PagedEverythingResults = (_, offset, count) =>
            {
                if (offset == 100)
                {
                    pageStarted.SetResult();
                    Assert.IsTrue(releasePage.Wait(TimeSpan.FromSeconds(5)));
                }
                else if (sortedStarted.Task.IsCompleted || pageStarted.Task.IsCompleted)
                {
                    sortedStarted.TrySetResult();
                    Assert.IsTrue(releaseSorted.Wait(TimeSpan.FromSeconds(5)));
                }
                return Enumerable.Range(offset, count).Select(i => new SearchViewItem
                {
                    ItemDisplayName = $"file_{i}.txt", OnlyKey = $"C:\\Files\\file_{i}.txt"
                }).ToList();
            };

            try
            {
                model.Search = "@sample";
                await WaitForAsync(() => model.Items.Count == 100 && model.LoadMoreEverythingCommand.CanExecute(null));
                var oldPage = model.LoadMoreEverythingAsync();
                await pageStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                model.EverythingSortOrder = EverythingSortOrder.SizeDescending;
                await sortedStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

                releasePage.Set();
                await oldPage;
                Assert.AreEqual(0, model.Items.Count);
                Assert.IsFalse(model.LoadMoreEverythingCommand.CanExecute(null));

                releaseSorted.Set();
                await WaitForAsync(() => model.Items.Count == 100 && model.LoadMoreEverythingCommand.CanExecute(null));
                Assert.AreEqual(0, SearchServicesProxy.EverythingCalls.Last().Offset);
                Assert.AreEqual(EverythingSortOrder.SizeDescending, SearchServicesProxy.EverythingCalls.Last().SortOrder);
            }
            finally
            {
                releasePage.Set();
                releaseSorted.Set();
            }
        });
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100; i++)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition()) return;
            await Task.Delay(20);
        }
        Assert.Fail("Timed out waiting for Everything search results.");
    }

    private static async Task RunAsync(Func<SearchWindow, SearchWindowViewModel, SearchServicesProxy, Task> test)
    {
        ReactiveUI.Builder.RxAppBuilder.CreateReactiveUIBuilder().WithPlatformServices().BuildApp();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EverythingFileListControlTests));
        await session.Dispatch(async () =>
        {
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
                    useEverything = false,
                    enableSemanticSearch = false,
                    semanticSearchDebounceMilliseconds = 100,
                    everythingSearchPreString = "@"
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
                SearchServicesProxy.EverythingResults = [];
                SearchServicesProxy.PagedEverythingResults = null;
                SearchServicesProxy.EverythingCalls.Clear();
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
        public static List<SearchViewItem> EverythingResults { get; set; } = [];
        public static Func<string, int, int, List<SearchViewItem>>? PagedEverythingResults { get; set; }
        public static List<(string Keyword, int Offset, int Count, EverythingSortOrder SortOrder)> EverythingCalls { get; set; } = [];
        public TaskCompletionSource<SearchViewItem> Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<SearchIndexResult> Results { get; set; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod!.Name switch
        {
            nameof(IWindowTool.MoveWindowToMouseScreenCenter) or nameof(IWindowTool.SetForegroundWindow)
                or nameof(IAppToolService.LoadIcon) => null,
            nameof(IIndexService.SearchPinyin) => Results,
            nameof(IIndexService.SearchAsync) => Task.FromResult(Results),
            nameof(IAppToolService.SearchWithEverything) => HandleEverythingSearch(args),
            nameof(ISearchItemTool.OpenFile) => Opened.TrySetResult((SearchViewItem)args![0]!),
            _ => throw new NotSupportedException(targetMethod.Name)
        };

        private static object HandleEverythingSearch(object?[]? args)
        {
            var kw = (string)args![0]!;
            var offset = 0;
            var count = 100;
            if (args.Length == 2)
            {
                count = (int)args[1]!;
            }
            else if (args.Length >= 3)
            {
                offset = (int)args[1]!;
                count = (int)args[2]!;
            }
            var sortOrder = args.Length >= 4 ? (EverythingSortOrder)args[3]! : EverythingSortOrder.NameAscending;
            EverythingCalls.Add((kw, offset, count, sortOrder));
            if (PagedEverythingResults != null)
            {
                return PagedEverythingResults(kw, offset, count);
            }
            return EverythingResults;
        }
    }
}
