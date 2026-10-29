using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Messaging;
using Kitopia.Desktop.Features.Search.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Kitopia.Desktop.Windows;

public partial class SearchWindow : Window
{
    private readonly double _preferredMaxHeight;
    private Screen? _placementScreen;
    private SearchViewItem? _lastPinnedItem;
    private SearchViewItem? _focusedSearchItem;
    private PixelRect? _directorySearchArea;

    public SearchWindow()
    {
        InitializeComponent();
        _preferredMaxHeight = MaxHeight;
        WeakReferenceMessenger.Default.Register<string, string>(this, "SearchWindowClose",
            (_, _) => { Dispatcher.UIThread.Invoke(() => { IsVisible = false; }); });
        #if DEBUG
        Topmost = false;
        #endif
        tx.GotFocus += (_, e) =>
        {
            if (!ReferenceEquals(e.Source, tx)) return;
            _focusedSearchItem = null;
            if (DataContext is SearchWindowViewModel { IsPreviewMode: true }) return;
            PinnedList.SelectedIndex = -1;
            dataGrid.SelectedIndex = -1;
            everythingControl.ClearSelection();
        };
        PinnedList.AddHandler(KeyDownEvent, SearchItems_OnKeyDown, RoutingStrategies.Tunnel);
        dataGrid.AddHandler(KeyDownEvent, SearchItems_OnKeyDown, RoutingStrategies.Tunnel);
        PinnedList.AddHandler(TextInputEvent, SearchItems_OnTextInput, RoutingStrategies.Tunnel);
        dataGrid.AddHandler(TextInputEvent, SearchItems_OnTextInput, RoutingStrategies.Tunnel);
        dataGrid.GotFocus += (_, e) =>
        {
            if (e.Source is ListBoxItem { DataContext: SearchViewItem item }) _focusedSearchItem = item;
        };
        dataGrid.GetObservable(ListBox.SelectedItemProperty).Subscribe(selected =>
        {
            if (selected is null || !ReferenceEquals(selected, _focusedSearchItem)) return;
            Dispatcher.UIThread.Post(() =>
            {
                var topLevel = TopLevel.GetTopLevel(dataGrid);
                if (dataGrid.IsEffectivelyVisible && topLevel is Window { IsActive: true }
                    && topLevel.FocusManager?.GetFocusedElement() is null && ReferenceEquals(dataGrid.SelectedItem, selected))
                    FocusItem(dataGrid, dataGrid.SelectedIndex);
            }, DispatcherPriority.Loaded);
        });
        FilePreviewSurface.CloseRequested += () =>
            (DataContext as SearchWindowViewModel)?.ClosePreviewMode();

        everythingControl.ItemActivated += (_, item) =>
        {
            (DataContext as SearchWindowViewModel)?.ActivateItem(item);
        };
        everythingControl.SortChanged += (_, sortOrder) =>
        {
            if (DataContext is SearchWindowViewModel model)
                model.EverythingSortOrder = sortOrder;
        };
        everythingControl.NavigateUpRequested += (_, _) =>
        {
            if (PinnedList.IsEffectivelyVisible && PinnedList.Items.Count > 0)
                FocusItem(PinnedList, Math.Max(0, PinnedList.Items.IndexOf(_lastPinnedItem)));
            else
                tx.Focus();
        };
        everythingControl.KeyDownRedirected += (_, e) =>
        {
            tx.Focus();
            e.Handled = true;
            tx.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = KeyDownEvent,
                Key = e.Key,
                KeyModifiers = e.KeyModifiers,
                PhysicalKey = e.PhysicalKey,
                KeySymbol = e.KeySymbol
            });
        };
        everythingControl.TextInputRedirected += (_, e) =>
        {
            tx.Focus();
            e.Handled = true;
            tx.RaiseEvent(new TextInputEventArgs { RoutedEvent = TextInputEvent, Text = e.Text });
        };
        everythingControl.GetObservable(Controls.EverythingFileListControl.SelectedItemProperty).Subscribe(selected =>
        {
            if (selected != null)
                PinnedList.SelectedIndex = -1;
        });


        SizeChanged += (_, _) =>
        {
            if (IsVisible) PositionWindow();
        };
        this.GetObservable(IsVisibleProperty).Subscribe(visible =>
        {
            if (DataContext is SearchWindowViewModel model)
            {
                model.SetProcessUsageEnabled(visible);
                if (visible) _ = model.CheckEverythingAvailabilityAsync();
                else model.CancelEverythingAvailabilityCheck();
            }
            if (!visible) EverythingStatusIndicator.Flyout?.Hide();
        });
    }

    public override void Show()
    {
        _directorySearchArea = null;
        MinHeight = 180;
        MinWidth = MaxWidth = Width = 960;
        if (DataContext is SearchWindowViewModel { IsDirectorySearch: true } model)
        {
            model.DirectorySearchPath = null;
            if (model.Search.Length == 0) model.ToSearch(string.Empty);
            else model.Search = string.Empty;
        }
        _placementScreen = null;
        if (!IsLoaded) base.Show();
        var windowTool = ServiceManager.Services.GetRequiredService<IWindowTool>();
        windowTool.MoveWindowToMouseScreenCenter(this);
        _placementScreen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (_placementScreen is { } screen)
            MaxHeight = Math.Min(_preferredMaxHeight, screen.WorkingArea.Height / screen.Scaling);
        base.Show();
        PositionWindow();
        windowTool.SetForegroundWindow(TryGetPlatformHandle()!.Handle);
        tx.Focus();
        tx.SelectAll();
        _lastPinnedItem = null;
        PinnedList.SelectedIndex = -1;
        dataGrid.SelectedIndex = -1;
            everythingControl.ClearSelection();
        ((SearchWindowViewModel?)DataContext)?.LoadLast();
        ((SearchWindowViewModel?)DataContext)?.SetProcessUsageEnabled(IsVisible);
    }

    public void ShowDirectorySearch(string directory, string text, PixelRect explorerBounds)
    {
        _placementScreen = Screens.ScreenFromPoint(explorerBounds.Center) ?? Screens.Primary;
        if (_placementScreen is { } screen)
        {
            var area = explorerBounds.Intersect(screen.WorkingArea);
            if (area.Width <= 0 || area.Height <= 0) area = screen.WorkingArea;
            _directorySearchArea = area;
            MinHeight = 0;
            MaxHeight = Math.Max(1, Math.Min(540, area.Height / screen.Scaling - 32));
            MinWidth = 0;
            MaxWidth = Width = Math.Max(1, Math.Min(720, area.Width / screen.Scaling - 32));
        }
        if (DataContext is SearchWindowViewModel model)
        {
            model.NowInSelectMode = false;
            model.DirectorySearchPath = directory;
            if (model.Search == text) model.ToSearch(text);
            else model.Search = text;
        }
        base.Show();
        PositionWindow();
        ServiceManager.Services.GetRequiredService<IWindowTool>().SetForegroundWindow(TryGetPlatformHandle()!.Handle);
        tx.Focus();
        tx.CaretIndex = tx.Text?.Length ?? 0;
        tx.SelectionStart = tx.SelectionEnd = tx.CaretIndex;
    }

    private void PositionWindow()
    {
        if (_placementScreen is not { } screen) return;

        Position = GetSearchWindowPosition(_directorySearchArea ?? screen.WorkingArea,
            screen.Scaling, Bounds.Size, _directorySearchArea.HasValue);
    }

    internal static PixelPoint GetSearchWindowPosition(PixelRect area, double scaling, Size size, bool bottomRight = false)
    {
        var width = (int)Math.Ceiling(size.Width * scaling);
        var height = (int)Math.Ceiling(size.Height * scaling);
        if (bottomRight)
        {
            var margin = (int)Math.Ceiling(16 * scaling);
            return new PixelPoint(Math.Max(area.X, area.Right - width - margin),
                Math.Max(area.Y, area.Bottom - height - margin));
        }
        return new PixelPoint(
            area.X + Math.Max(0, (area.Width - width) / 2),
            Math.Clamp(area.Y + area.Height / 4, area.Y, Math.Max(area.Y, area.Bottom - height)));
    }

    private void w_Deactivated(object? sender, EventArgs eventArgs)
    {
        IsVisible = false;
    }


    private void w_Activated(object sender, EventArgs e)
    {
        Focus();
        tx.Focus();
    }

    private void tx_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (DataContext is SearchWindowViewModel viewModel)
            {
                if (PinnedList.SelectedItem is SearchViewItem pinnedItem)
                    viewModel.OpenFile(pinnedItem);
                else
    if (viewModel.IsEverythingMode)
                    viewModel.ActivateItem((SearchViewItem?)everythingControl.SelectedItem ?? viewModel.Items.FirstOrDefault());
                else
                    viewModel.ActivateItem((SearchViewItem?)dataGrid.SelectedItem);
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Down && e.KeyModifiers == KeyModifiers.None)
        {
            if (DataContext is SearchWindowViewModel { IsEverythingMode: true })
            {
                everythingControl.FocusFirstItem();
            }
            else
            {
                FocusItem(PinnedList.IsEffectivelyVisible && PinnedList.Items.Count > 0 ? PinnedList : dataGrid, 0);
            }
            e.Handled = true;
        }
    }

    private void SearchItems_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;

        if (ReferenceEquals(sender, PinnedList))
        {
            _lastPinnedItem = (SearchViewItem?)PinnedList.SelectedItem;
            dataGrid.SelectedIndex = -1;
            everythingControl.ClearSelection();
        }
        else
        {
            PinnedList.SelectedIndex = -1;
        }
    }

    private static void FocusItem(ListBox list, int index)
    {
        if (index < 0 || index >= list.Items.Count) return;

        list.SelectedIndex = index;
        list.ScrollIntoView(index);
        list.UpdateLayout();
        (list.ContainerFromIndex(index) ?? (Control)list).Focus(NavigationMethod.Directional);
    }

    private void SearchItems_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not ListBox list || (e.Source is not ListBoxItem && !ReferenceEquals(e.Source, list))) return;

        if (e.Key == Key.Enter)
        {
            if (DataContext is SearchWindowViewModel viewModel)
            {
                if (ReferenceEquals(list, PinnedList))
                    viewModel.OpenFile((SearchViewItem?)list.SelectedItem);
                else
                    viewModel.ActivateItem((SearchViewItem?)list.SelectedItem);
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Apps || (e.Key == Key.F10 && e.KeyModifiers == KeyModifiers.Shift))
        {
            var button = list.ContainerFromIndex(list.SelectedIndex)?.GetVisualDescendants().OfType<Button>().FirstOrDefault();
            button?.ContextFlyout?.ShowAt(button);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up && e.KeyModifiers == KeyModifiers.None
            && (ReferenceEquals(list, PinnedList) || list.SelectedIndex <= 0))
        {
            if (ReferenceEquals(list, dataGrid) && PinnedList.IsEffectivelyVisible && PinnedList.Items.Count > 0)
                FocusItem(PinnedList, Math.Max(0, PinnedList.Items.IndexOf(_lastPinnedItem)));
            else
                tx.Focus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down && e.KeyModifiers == KeyModifiers.None && ReferenceEquals(list, PinnedList))
        {
            if (DataContext is SearchWindowViewModel { IsEverythingMode: true })
                everythingControl.FocusFirstItem();
            else
                FocusItem(dataGrid, 0);
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Up or Key.Down or Key.Left or Key.Right or Key.Home or Key.End or Key.Tab
            or Key.PageDown or Key.PageUp or Key.Escape or Key.LeftShift or Key.RightShift
            or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin
            or >= Key.F1 and <= Key.F24) return;

        // The original event route still points at the list after focus changes.
        tx.Focus();
        e.Handled = true;
        tx.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = KeyDownEvent,
            Key = e.Key,
            KeyModifiers = e.KeyModifiers,
            PhysicalKey = e.PhysicalKey,
            KeySymbol = e.KeySymbol
        });
    }

    private void SearchItems_OnTextInput(object? sender, TextInputEventArgs e)
    {
        tx.Focus();
        e.Handled = true;
        tx.RaiseEvent(new TextInputEventArgs { RoutedEvent = TextInputEvent, Text = e.Text });
    }

    private void InputElement_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;

        if (DataContext is SearchWindowViewModel { IsPreviewMode: true } viewModel)
        {
            viewModel.ClosePreviewMode();
            e.Handled = true;
            return;
        }

        IsVisible = false;
        e.Handled = true;
    }

    private void SearchItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, e.Source) && sender is Button { DataContext: SearchViewItem item }
            && DataContext is SearchWindowViewModel viewModel)
        {
            viewModel.ClickItem(item);
        }
    }

    private void Window_OnClosing(object? sender, WindowClosingEventArgs e)
    {
        e.Cancel = true;
        IsVisible = false;
    }

    private void HorizonScroll(object? sender, PointerWheelEventArgs pointerWheelEventArgs)
    {
        var scrollViewer = sender as ScrollViewer
                           ?? (sender as ListBox)?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scrollViewer is not null)
        {
            // 获取滚轮滚动的增量值
            var delta = pointerWheelEventArgs.Delta.Y;

            // 调整滚动条的横向偏移
            scrollViewer.Offset = new Vector(scrollViewer.Offset.X - delta * 20, scrollViewer.Offset.Y);

            // 标记事件为已处理，防止默认的垂直滚动
            pointerWheelEventArgs.Handled = true;
        }
    }

    private void ToggleButton_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        Task.Run(() =>
        {
            Dispatcher.UIThread.InvokeAsync(() => { ((SearchWindowViewModel)DataContext).UpdateFilter(); });
        });
    }
}
