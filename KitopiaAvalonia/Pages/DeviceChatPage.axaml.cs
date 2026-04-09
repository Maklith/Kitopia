using System;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Core.ViewModel.Pages.device;

namespace KitopiaAvalonia.Pages;

public partial class DeviceChatPage : UserControl
{
    private DeviceChatPageViewModel? _viewModel;
    private bool _isAttachedToVisualTree;

    public DeviceChatPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        PropertyChanged += OnControlPropertyChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.SetChatInterfaceActive(false, null);
            _viewModel.Messages.CollectionChanged -= OnMessagesCollectionChanged;
        }

        _viewModel = DataContext as DeviceChatPageViewModel;
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.Messages.CollectionChanged += OnMessagesCollectionChanged;
        UpdateChatActiveState();
        ScheduleScrollToBottom();
    }

    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!ShouldAutoScrollForChange(e))
        {
            return;
        }

        ScheduleScrollToBottom();
    }

    private void OnControlPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty && e.NewValue is bool isVisible && isVisible)
        {
            ScheduleScrollToBottom();
        }

        if (e.Property == IsVisibleProperty)
        {
            UpdateChatActiveState();
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttachedToVisualTree = true;
        UpdateChatActiveState();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttachedToVisualTree = false;
        _viewModel?.SetChatInterfaceActive(false, null);
    }

    private void UpdateChatActiveState()
    {
        if (_viewModel is null)
        {
            return;
        }

        var isActive = IsVisible && _isAttachedToVisualTree;
        var device = isActive ? _viewModel.GetCurrentChatDevice() : null;
        _viewModel.SetChatInterfaceActive(isActive, device);
    }

    private void ScheduleScrollToBottom()
    {
        Dispatcher.UIThread.Post(ScrollToBottom, DispatcherPriority.Background);
        // Progress updates are frequent; run once again after layout to avoid partial clipping.
        Dispatcher.UIThread.Post(ScrollToBottom, DispatcherPriority.Loaded);
    }

    private bool ShouldAutoScrollForChange(NotifyCollectionChangedEventArgs e)
    {
        var totalCount = _viewModel?.Messages.Count ?? 0;
        return e.Action switch
        {
            NotifyCollectionChangedAction.Reset => true,
            NotifyCollectionChangedAction.Add =>
                e.NewItems is { Count: > 0 } &&
                e.NewStartingIndex >= 0 &&
                e.NewStartingIndex + e.NewItems.Count >= totalCount,
            NotifyCollectionChangedAction.Replace =>
                e.NewItems is { Count: > 0 } &&
                e.NewStartingIndex >= 0 &&
                e.NewStartingIndex + e.NewItems.Count >= totalCount,
            NotifyCollectionChangedAction.Remove =>
                e.OldItems is { Count: > 0 } &&
                e.OldStartingIndex >= 0 &&
                e.OldStartingIndex >= totalCount,
            NotifyCollectionChangedAction.Move =>
                e.NewItems is { Count: > 0 } &&
                e.NewStartingIndex >= 0 &&
                e.NewStartingIndex + e.NewItems.Count >= totalCount,
            _ => false
        };
    }

    private void ScrollToBottom()
    {
        var scrollViewer = MessagesListBox
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault();
        if (scrollViewer is null)
        {
            return;
        }

        var targetY = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        scrollViewer.Offset = new Vector(scrollViewer.Offset.X, targetY);
    }
    

    private async void MessageInputBox_OnPastingFromClipboard(object? sender, RoutedEventArgs e)
    {
        bool shouldHandle = await _viewModel?.TrySendClipboardTransferForInputPasteAsync();
        if (shouldHandle)
        {
            e.Handled = true;
        }
    }
}
