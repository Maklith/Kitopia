using System;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Kitopia.Desktop.Features.Utils;
using Kitopia.Desktop.Features.Search.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Kitopia.Desktop.Windows;

public partial class SearchWindow : Window
{
    private INotifyCollectionChanged? _currentItemsSource;

    public SearchWindow()
    {
        InitializeComponent();
        WeakReferenceMessenger.Default.Register<string, string>(this, "SearchWindowClose",
            (_, _) => { Dispatcher.UIThread.InvokeAsync(() => { IsVisible = false; }); });
        #if DEBUG
        Topmost = false;
        #endif
        dataGrid.PropertyChanged += DataGridOnPropertyChanged;
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
            if (dataGrid.Items.Count > 0)
                dataGrid.SelectedItem = dataGrid.Items[0];
        });
    }

    public override void Show()
    {
        if (!IsLoaded) base.Show();
        ServiceManager.Services.GetService<IWindowTool>()!.MoveWindowToMouseScreenCenter(this);
        base.Show();
        ServiceManager.Services.GetService<IWindowTool>()!.MoveWindowToMouseScreenCenter(this);
        ServiceManager.Services.GetService<IWindowTool>()!.SetForegroundWindow(
            TryGetPlatformHandle()!.Handle);
        tx.Focus();
        tx.SelectAll();
        ((SearchWindowViewModel?)DataContext)?.LoadLast();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var size = desktop.MainWindow.Screens.Primary.Bounds.Size;
            Position = new PixelPoint((int)((size.Width - Width) / 2), size.Height / 4);
        }
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
            if (dataGrid.Items.Count != 0)
                ServiceManager.Services!.GetService<SearchWindowViewModel>()!.OpenFile(
                    (SearchViewItem)dataGrid.Items[0]);

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
            ((SearchWindowViewModel)DataContext).OpenFile(item);
            return;
        }

        if (e.Key != Key.Down && e.Key != Key.Home && e.Key != Key.End &&
            e.Key != Key.Left && e.Key != Key.Right && e.Key != Key.Tab && e.Key != Key.PageDown && e.Key != Key.PageUp)
            tx.Focus();
    }

    private void DataGrid_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var listBoxItem = dataGrid.GetVisualAt<ListBoxItem>(e.GetCurrentPoint(dataGrid)
            .Position);
        if (listBoxItem != null) listBoxItem.IsSelected = true;
    }

    private void InputElement_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) IsVisible = false;
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
