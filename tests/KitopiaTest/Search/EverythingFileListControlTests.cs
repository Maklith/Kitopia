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
using Kitopia.Feature.Avalonia.Controls;
using Kitopia.Feature.Localization;
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

    [TestMethod]
    [DataRow(0)]
    [DataRow(100)]
    public async Task SearchWindow_EverythingQueryFailure_ShowsWarningAndRetriesSamePage(int offset)
    {
        await RunAsync(async (window, model, _) =>
        {
            var indicator = window.FindControl<HoverFlyoutButton>("EverythingStatusIndicator")!;
            Assert.IsNull(model.IsEverythingAvailable);
            Assert.IsFalse(indicator.IsVisible);
            if (offset > 0)
            {
                SearchServicesProxy.PagedEverythingResults = (_, _, count) => Enumerable.Range(0, count)
                    .Select(i => new SearchViewItem { OnlyKey = $"file-{i}", ItemDisplayName = $"file-{i}" }).ToList();
                model.Search = "@missing";
                await WaitForAsync(() => model.Items.Count == offset && model.LoadMoreEverythingCommand.CanExecute(null));
            }
            SearchServicesProxy.PagedEverythingResults = (_, _, _) => throw new InvalidOperationException("Everything IPC failed");
            if (offset == 0) model.Search = "@missing";
            else await model.LoadMoreEverythingAsync();
            await WaitForAsync(() => model.IsEverythingAvailable is false && model.LoadMoreEverythingCommand.CanExecute(null));
            Assert.IsTrue(indicator.IsVisible);
            Assert.IsTrue(indicator.Classes.Contains("unavailable"));
            Assert.AreEqual(offset, model.Items.Count);

            indicator.Flyout!.ShowAt(indicator);
            Dispatcher.UIThread.RunJobs();
            SearchServicesProxy.PagedEverythingResults = (_, _, _) => [];
            var retry = window.FindControl<Button>("RetryEverythingSearchButton")!;
            Assert.AreSame(model.LoadMoreEverythingCommand, retry.Command);
            await model.LoadMoreEverythingCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(true, model.IsEverythingAvailable);
            Assert.IsTrue(model.IsEverythingConnected);
            Assert.IsFalse(indicator.Classes.Contains("unavailable"));
            Assert.IsFalse(model.LoadMoreEverythingCommand.CanExecute(null));
            Assert.AreEqual(offset, model.Items.Count);
            Assert.AreEqual(offset, SearchServicesProxy.EverythingCalls.Last().Offset);
        });
    }

    [TestMethod]
    public async Task SearchWindow_NewQuery_CancelsPlatformTokenAndIgnoresOldFailure()
    {
        await RunAsync(async (_, model, _) =>
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var release = new ManualResetEventSlim();
            SearchServicesProxy.PagedEverythingResults = (query, offset, _) =>
            {
                if (query == "old" && offset == 100)
                {
                    started.SetResult();
                    Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(5)));
                    throw new InvalidOperationException("Old query failed");
                }
                if (query == "old")
                {
                    return Enumerable.Range(0, 100).Select(i => new SearchViewItem
                    {
                        OnlyKey = $"old-{i}", ItemDisplayName = $"old-{i}"
                    }).ToList();
                }
                return [new SearchViewItem { OnlyKey = "new", ItemDisplayName = "new" }];
            };
            Task? oldPage = null;
            try
            {
                model.Search = "@old";
                await WaitForAsync(() => model.Items.Count == 100 && model.LoadMoreEverythingCommand.CanExecute(null));
                oldPage = model.LoadMoreEverythingAsync();
                await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var token = SearchServicesProxy.EverythingCalls.Last().CancellationToken;
                Assert.IsTrue(token.CanBeCanceled);
                model.Search = "@new";
                await WaitForAsync(() => model.Items.Count == 1 && model.LoadMoreEverythingCommand.CanExecute(null) == false);
                Assert.IsTrue(token.IsCancellationRequested);
                release.Set();
                await oldPage.WaitAsync(TimeSpan.FromSeconds(5));
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(true, model.IsEverythingAvailable);
                Assert.AreEqual("new", model.Items.Single().OnlyKey);
            }
            finally
            {
                release.Set();
                if (oldPage is not null) await oldPage;
            }
        });
    }

    [TestMethod]
    [DataRow("available")]
    [DataRow("unavailable")]
    [DataRow("failure")]
    public async Task SearchWindow_Opens_ChecksEverythingOffUiThreadAndRefreshesOnReopen(string outcome)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var calls = 0;
        try
        {
            await RunAsync(async (window, model, _) =>
            {
                await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var indicator = window.FindControl<HoverFlyoutButton>("EverythingStatusIndicator")!;
                Assert.IsTrue(model.IsCheckingEverything);
                Assert.IsTrue(indicator.IsVisible);
                Assert.IsTrue(indicator.Classes.Contains("checking"));
                Assert.IsTrue(window.FindControl<TextBox>("tx")!.IsEnabled);
                Assert.IsFalse(model.CheckEverythingAvailabilityCommand.CanExecute(null));
                await model.CheckEverythingAvailabilityAsync();
                Assert.AreEqual(1, Volatile.Read(ref calls));

                release.Set();
                await WaitForAsync(() => !model.IsCheckingEverything);
                Assert.AreEqual(outcome == "available", model.IsEverythingAvailable);
                Assert.AreEqual(outcome != "available", indicator.IsVisible);
                Assert.IsFalse(model.IsEverythingMode);
                Assert.AreEqual(0, SearchServicesProxy.EverythingCalls.Count);

                window.Hide();
                window.Show();
                await WaitForAsync(() => Volatile.Read(ref calls) == 2 && !model.IsCheckingEverything);
            }, token =>
            {
                Assert.IsFalse(Dispatcher.UIThread.CheckAccess());
                Interlocked.Increment(ref calls);
                started.TrySetResult();
                Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(5), token));
                if (outcome == "failure") throw new InvalidOperationException("Probe failed");
                return outcome == "available";
            });
        }
        finally { release.Set(); }
    }

    [TestMethod]
    public async Task SearchWindow_EmptyEverythingPrefix_ProbesAndAllowsCheckingAgainWithoutSearching()
    {
        var calls = 0;
        await RunAsync(async (window, model, _) =>
        {
            await WaitForAsync(() => model.IsEverythingAvailable is true && !model.IsCheckingEverything);
            model.Search = "@";
            await WaitForAsync(() => model.IsEverythingUnavailable);
            Assert.AreEqual(2, Volatile.Read(ref calls));
            Assert.AreEqual(0, SearchServicesProxy.EverythingCalls.Count);
            Assert.IsFalse(model.LoadMoreEverythingCommand.CanExecute(null));

            var indicator = window.FindControl<HoverFlyoutButton>("EverythingStatusIndicator")!;
            indicator.Flyout!.ShowAt(indicator);
            Dispatcher.UIThread.RunJobs();
            var check = window.FindControl<Button>("CheckEverythingAvailabilityButton")!;
            Assert.AreSame(model.CheckEverythingAvailabilityCommand, check.Command);
            await model.CheckEverythingAvailabilityCommand.ExecuteAsync(null);
            Assert.IsTrue(model.IsEverythingConnected);
            Assert.IsTrue(indicator.IsVisible);
            Assert.IsFalse(indicator.Classes.Contains("unavailable"));
            Assert.AreEqual(0, SearchServicesProxy.EverythingCalls.Count);
            window.Hide();
            Assert.IsFalse(indicator.Flyout.IsOpen);
        }, _ => Interlocked.Increment(ref calls) != 2);
    }

    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, false)]
    [DataRow(true, true)]
    public async Task SearchWindow_NewQuery_DiscardsCanceledAvailabilityResult(bool searchSucceeds, bool probeThrows)
    {
        var calls = 0;
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        await RunAsync(async (_, model, _) =>
        {
            await WaitForAsync(() => model.IsEverythingAvailable is true && !model.IsCheckingEverything);
            var oldCheck = model.CheckEverythingAvailabilityAsync();
            try
            {
                var token = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                SearchServicesProxy.PagedEverythingResults = (_, _, _) => searchSucceeds
                    ? [new SearchViewItem { OnlyKey = "new", ItemDisplayName = "new" }]
                    : throw new InvalidOperationException("New query failed");
                model.Search = "@new";
                await WaitForAsync(() => !model.IsCheckingEverything && model.IsEverythingAvailable == searchSucceeds
                                         && (searchSucceeds ? model.Items.Count == 1 : model.LoadMoreEverythingCommand.CanExecute(null)));
                Assert.IsTrue(token.IsCancellationRequested);
                release.Set();
                await oldCheck.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.AreEqual(searchSucceeds, model.IsEverythingAvailable);
                Assert.AreEqual(searchSucceeds ? 1 : 0, model.Items.Count);
            }
            finally
            {
                release.Set();
                await oldCheck;
            }
        }, _ =>
        {
            if (Interlocked.Increment(ref calls) == 1) return true;
            started.TrySetResult(_);
            Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(5)));
            if (probeThrows) throw new InvalidOperationException("Old probe failed");
            return !searchSucceeds;
        });
    }

    [TestMethod]
    [DataRow("zh-CN", false)]
    [DataRow("zh-CN", true)]
    [DataRow("en-US", false)]
    [DataRow("en-US", true)]
    public async Task SearchWindow_EverythingStatusCard_UsesThemeAndKeepsRetryReachable(string language, bool dark)
    {
        await RunAsync(async (window, model, _) =>
        {
            Lang.Current.UseLanguage(language);
            var theme = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            Application.Current!.RequestedThemeVariant = theme;
            window.RequestedThemeVariant = theme;
            // Headless hosts popups inside the window instead of native surfaces outside it.
            window.SizeToContent = SizeToContent.Manual;
            window.Height = 420;
            SearchServicesProxy.PagedEverythingResults = (_, _, _) => throw new InvalidOperationException("Everything unavailable");
            model.Search = "@Kitopia";
            await WaitForAsync(() => model.IsEverythingUnavailable && model.LoadMoreEverythingCommand.CanExecute(null));
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var indicator = window.FindControl<HoverFlyoutButton>("EverythingStatusIndicator")!;
            Assert.IsTrue(indicator.Bounds.Height <= 28);
            Assert.AreEqual(28, indicator.Bounds.Width);
            var point = indicator.TranslatePoint(new Point(indicator.Bounds.Width / 2, indicator.Bounds.Height / 2), window)!.Value;
            window.MouseMove(point);
            Dispatcher.UIThread.RunJobs();
            Assert.IsTrue(indicator.Flyout!.IsOpen);

            var retry = window.FindControl<Button>("RetryEverythingSearchButton")!;
            var card = window.FindControl<HeaderedContentControl>("EverythingWarningCard")!;
            Assert.AreEqual(window.FindResource("KitopiaWarningIndicatorButton"), indicator.Theme);
            Assert.AreEqual(window.FindResource("KitopiaWarningCard"), card.Theme);
            Assert.AreEqual(Avalonia.Media.Color.Parse(dark ? "#33B74700" : "#FFF4CE"),
                ((Avalonia.Media.ISolidColorBrush)card.Background!).Color);
            Assert.AreEqual((byte)255, ((Avalonia.Media.ISolidColorBrush)card.GetVisualDescendants()
                .OfType<Border>().First().Background!).Color.A, "Popup text must not overlap content behind the warning.");
            var description = window.FindControl<TextBlock>("EverythingUnavailableDescription")!;
            Assert.AreEqual(Lang.Get("lang.kitopia.everything_unavailable_description"), description.Text);
            Assert.AreEqual(Avalonia.Media.TextWrapping.Wrap, description.TextWrapping);
            var popup = TopLevel.GetTopLevel(retry)!;
            popup.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.IsTrue(retry.Bounds.Width > 0);
            var screenshotDirectory = Path.Combine(AppContext.BaseDirectory, "TestResults", "everything-status");
            Directory.CreateDirectory(screenshotDirectory);
            foreach (var (surface, name) in new[] { ((TopLevel)window, "search"), (popup, "details") })
            {
                using var frame = surface.CaptureRenderedFrame();
                Assert.IsNotNull(frame);
                var path = Path.Combine(screenshotDirectory, $"everything-status-{language}-{theme}-{name}.png");
                frame.Save(path, PngBitmapEncoderOptions.Default);
                TestContext.AddResultFile(path);
            }

            var retryPoint = retry.TranslatePoint(new Point(retry.Bounds.Width / 2, retry.Bounds.Height / 2), popup)!.Value;
            Assert.IsNotNull(popup.InputHitTest(retryPoint),
                $"Retry must fit inside popup host. Window: {window.Bounds}; client: {window.ClientSize}; max: {window.MaxHeight}; popup: {popup.Bounds}; retry: {retryPoint}");
            window.MouseMove(new Point(window.Width - 10, window.Height - 10));
            popup.MouseMove(retryPoint);
            await Task.Delay(250);
            Assert.IsTrue(indicator.Flyout.IsOpen,
                $"Window visible: {window.IsVisible}; retry pointer: {retry.IsPointerOver}; content pointer: {((Flyout)indicator.Flyout).Content is Control content && content.IsPointerOver}; hit: {popup.InputHitTest(retryPoint)}; point: {retryPoint}; retry bounds: {retry.Bounds}");
            SearchServicesProxy.PagedEverythingResults = (_, _, _) => [];
            popup.MouseDown(retryPoint, MouseButton.Left);
            popup.MouseUp(retryPoint, MouseButton.Left);
            await WaitForAsync(() => model.IsEverythingConnected);
            Assert.IsFalse(indicator.Classes.Contains("unavailable"));
            window.Hide();
            Assert.IsFalse(indicator.Flyout.IsOpen);
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

    private static async Task RunAsync(Func<SearchWindow, SearchWindowViewModel, SearchServicesProxy, Task> test,
        Func<CancellationToken, bool>? availability = null)
    {
        ReactiveUI.Builder.RxAppBuilder.CreateReactiveUIBuilder().WithPlatformServices().BuildApp();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EverythingFileListControlTests));
        await session.Dispatch(async () =>
        {
            WeakReferenceMessenger.Default.Reset();
            var originalConfigs = ConfigManger.Configs;
            var originalServices = ServiceManager.Services;
            var originalTheme = Application.Current!.RequestedThemeVariant;
            var originalLanguage = Lang.Current.Language;
            var identifiers = PluginOverall.SearchWindowInputDataIdentifies.ToArray();
            var analyzers = PluginOverall.SearchWindowInputDataAnalyzers.ToArray();
            ConfigManger.Configs = new Dictionary<string, ConfigBase>
            {
                ["KitopiaConfig"] = new KitopiaConfig
                {
                    useEverythingForIndexing = false,
                    enableSemanticSearch = false,
                    semanticSearchDebounceMilliseconds = 100,
                    everythingSearchPrefix = "@"
                }
            };
            PluginOverall.SearchWindowInputDataIdentifies.Clear();
            PluginOverall.SearchWindowInputDataAnalyzers.Clear();
            PluginOverall.SearchWindowInputDataIdentifies["Kitopia"] = [];
            PluginOverall.SearchWindowInputDataAnalyzers["Kitopia"] = [];
            var itemTool = DispatchProxy.Create<ISearchItemTool, SearchServicesProxy>();
            var registrations = new ServiceCollection()
                .AddSingleton(DispatchProxy.Create<IWindowTool, SearchServicesProxy>())
                .AddSingleton(DispatchProxy.Create<IAppToolService, SearchServicesProxy>())
                .AddSingleton(DispatchProxy.Create<IIndexService, SearchServicesProxy>())
                .AddSingleton(itemTool);
            if (availability is not null)
            {
                var probe = DispatchProxy.Create<IEverythingAvailabilityService, SearchServicesProxy>();
                ((SearchServicesProxy)probe).AvailabilityProbe = availability;
                registrations.AddSingleton(probe);
            }
            using var services = registrations.BuildServiceProvider();
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
                Lang.Current.UseLanguage(originalLanguage);
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
        public static List<(string Keyword, int Offset, int Count, EverythingSortOrder SortOrder, CancellationToken CancellationToken)> EverythingCalls { get; set; } = [];
        public TaskCompletionSource<SearchViewItem> Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<SearchIndexResult> Results { get; set; } = [];
        public Func<CancellationToken, bool>? AvailabilityProbe { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod!.Name switch
        {
            nameof(IWindowTool.MoveWindowToMouseScreenCenter) or nameof(IWindowTool.SetForegroundWindow)
                or nameof(IAppToolService.LoadIcon) => null,
            nameof(IIndexService.SearchPinyin) => Results,
            nameof(IIndexService.SearchAsync) => Task.FromResult(Results),
            nameof(IAppToolService.SearchWithEverything) => HandleEverythingSearch(args),
            nameof(IEverythingAvailabilityService.IsAvailable) => AvailabilityProbe!.Invoke((CancellationToken)args![0]!),
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
            var token = args.Length >= 5 ? (CancellationToken)args[4]! : CancellationToken.None;
            EverythingCalls.Add((kw, offset, count, sortOrder, token));
            if (PagedEverythingResults != null)
            {
                return PagedEverythingResults(kw, offset, count);
            }
            return EverythingResults;
        }
    }
}
