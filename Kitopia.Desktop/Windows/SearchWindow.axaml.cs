using System;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Kitopia.Desktop.Features.Search.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Kitopia.Desktop.Windows;

public partial class SearchWindow : Window
{
    private readonly double _preferredMaxHeight;
    private Screen? _placementScreen;
    private INotifyCollectionChanged? _currentItemsSource;

    public SearchWindow()
    {
        InitializeComponent();
        _preferredMaxHeight = MaxHeight;
        WeakReferenceMessenger.Default.Register<string, string>(this, "SearchWindowClose",
            (_, _) => { Dispatcher.UIThread.Invoke(() => { IsVisible = false; }); });
        #if DEBUG
        Topmost = false;
        #endif
        dataGrid.PropertyChanged += DataGridOnPropertyChanged;
        FilePreviewSurface.CloseRequested += () =>
            (DataContext as SearchWindowViewModel)?.ClosePreviewMode();
        SizeChanged += (_, _) =>
        {
            if (IsVisible) PositionWindow();
        };
    }

    private void DataGridOnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != ListBox.ItemsSourceProperty) return;

        if (_currentItemsSource is not null)
            _currentItemsSource.CollectionChanged -= ItemsSourceOnCollectionChanged;

        _currentItemsSource = e.NewValue is INotifyCollectionChanged collectionChanged
            ? collectionChanged
            : null;

        if (_currentItemsSource is not null)
            _currentItemsSource.CollectionChanged += ItemsSourceOnCollectionChanged;
    }

    private void ItemsSourceOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (dataGrid.SelectedItem is null && dataGrid.Items.Count > 0)
                dataGrid.SelectedItem = dataGrid.Items[0];
        });
    }

    public override void Show()
    {
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
        ((SearchWindowViewModel?)DataContext)?.LoadLast();
    }

    private void PositionWindow()
    {
        if (_placementScreen is not { } screen) return;

        Position = GetSearchWindowPosition(screen.WorkingArea, screen.Scaling, Bounds.Size);
    }

    internal static PixelPoint GetSearchWindowPosition(PixelRect area, double scaling, Size size)
    {
        var width = (int)Math.Ceiling(size.Width * scaling);
        var height = (int)Math.Ceiling(size.Height * scaling);
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
                viewModel.ActivateItem((SearchViewItem?)dataGrid.SelectedItem
                                       ?? dataGrid.Items.Cast<SearchViewItem?>().FirstOrDefault());
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            var realizedContainers = dataGrid.GetRealizedContainers();
            dataGrid.SelectedItem = (object)((SearchWindowViewModel)DataContext).Items[0];
            foreach (var realizedContainer in realizedContainers)
                if (realizedContainer.DataContext == dataGrid.SelectedItem)
                {
                    realizedContainer.Focus();
                    break;
                }
        }
    }

    private void DataGrid_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Up)
        {
            var realizedContainers = dataGrid.GetRealizedContainers();
            if (realizedContainers.First()
                    .DataContext == dataGrid.SelectedItem)
                tx.Focus();

            return;
        }

        if (e.Key == Key.Enter)
        {
            var item = (SearchViewItem?)dataGrid.SelectedItem;
            ((SearchWindowViewModel)DataContext).ActivateItem(item);
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Down && e.Key != Key.Home && e.Key != Key.End &&
            e.Key != Key.Left && e.Key != Key.Right && e.Key != Key.Tab && e.Key != Key.PageDown && e.Key != Key.PageUp)
            tx.Focus();
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
        if (sender is ScrollViewer scrollViewer)
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
