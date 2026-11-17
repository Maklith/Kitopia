using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Kitopia.Feature.Avalonia.DeviceCommunication.ViewModels;
using Kitopia.Feature.Localization;

namespace Kitopia.Feature.Avalonia.DeviceCommunication.Views;

public partial class DeviceCommunicationPage : UserControl
{
    private DeviceCommunicationPageViewModel? _boundViewModel;
    private INotifyCollectionChanged? _boundMessages;
    private ScrollViewer? _conversationScrollViewer;
    private ItemsControl? _conversationItemsControl;
    private Border? _imagePreviewOverlay;
    private Canvas? _imagePreviewCanvas;
    private Image? _imagePreviewContent;
    private TextBlock? _imagePreviewTitle;
    private Border? _fileDropOverlay;
    private readonly RelayCommand<DeviceChatMessageItem> _previewImageCommand;
    private readonly AsyncRelayCommand<SelectableTextBlock> _copyTextCommand;
    private (WeakReference<SelectableTextBlock> Target, string SelectedText)? _pendingContextSelection;
    private double _imagePreviewWidth;
    private double _imagePreviewHeight;
    private double _imagePreviewScale = 1d;
    private double _imagePreviewMinScale = 1d;
    private double _imagePreviewOffsetX;
    private double _imagePreviewOffsetY;
    private bool _isImagePreviewPanning;
    private Point _imagePreviewPanStartPoint;
    private double _imagePreviewPanStartOffsetX;
    private double _imagePreviewPanStartOffsetY;
    private IPointer? _imagePreviewCapturedPointer;

    public DeviceCommunicationPage()
    {
        _previewImageCommand = new RelayCommand<DeviceChatMessageItem>(ShowImagePreview);
        _copyTextCommand = new AsyncRelayCommand<SelectableTextBlock>(CopyMessageTextAsync, CanCopyMessageText);
        InitializeComponent();
        _conversationScrollViewer = this.FindControl<ScrollViewer>("ConversationScrollViewer");
        _conversationItemsControl = this.FindControl<ItemsControl>("ConversationItemsControl");
        _imagePreviewOverlay = this.FindControl<Border>("ImagePreviewOverlay");
        _imagePreviewCanvas = this.FindControl<Canvas>("ImagePreviewCanvas");
        _imagePreviewContent = this.FindControl<Image>("ImagePreviewContent");
        _imagePreviewTitle = this.FindControl<TextBlock>("ImagePreviewTitle");
        _fileDropOverlay = this.FindControl<Border>("FileDropOverlay");
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnPageSizeChanged;
        AddHandler(InputElement.PointerPressedEvent, OnMessagePointerPressedTunnel, RoutingStrategies.Tunnel);
        AddHandler(InputElement.PointerPressedEvent, OnPreviewImagePointerPressed);
        AddHandler(InputElement.DoubleTappedEvent, OnFileCardDoubleTapped);
        AddHandler(Control.ContextRequestedEvent, OnMessageContextRequestedTunnel, RoutingStrategies.Tunnel);
        AddHandler(Control.ContextRequestedEvent, OnMessageContextRequestedBubble);
        AddHandler(SelectableTextBlock.CopyingToClipboardEvent, OnSelectableTextBlockCopyingToClipboard);
    }

    private const double CompactWidthThreshold = 700d;

    private void OnPageSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_boundViewModel is not null)
        {
            _boundViewModel.IsCompact = Bounds.Width > 0 && Bounds.Width < CompactWidthThreshold;
        }
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        BindViewModel();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        BindViewModel();
        _boundViewModel?.SyncDisplayContext();
    }

    private void BindViewModel()
    {
        var viewModel = DataContext as DeviceCommunicationPageViewModel;
        if (ReferenceEquals(_boundViewModel, viewModel))
        {
            return;
        }

        if (_boundViewModel is not null)
        {
            _boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            UnbindCurrentMessages();
            _boundViewModel = null;
        }

        _boundViewModel = viewModel;
        if (_boundViewModel is not null)
        {
            _boundViewModel.PropertyChanged += OnViewModelPropertyChanged;
            BindCurrentMessages();
            SetConversationItemsSource(_boundViewModel.CurrentMessages);
            ScrollToLatest();
            _boundViewModel.IsCompact = Bounds.Width > 0 && Bounds.Width < CompactWidthThreshold;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DeviceCommunicationPageViewModel.CurrentMessages))
        {
            CloseImagePreview();
            BindCurrentMessages();
            SetConversationItemsSource(_boundViewModel?.CurrentMessages);
            ScrollToLatest();
            return;
        }

        if (e.PropertyName == nameof(DeviceCommunicationPageViewModel.MessageViewRefreshVersion))
        {
            RebuildCurrentMessageVisuals();
            return;
        }

        if (e.PropertyName == nameof(DeviceCommunicationPageViewModel.MessageListVersion))
        {
            ScrollToLatest();
        }
    }

    private void BindCurrentMessages()
    {
        UnbindCurrentMessages();

        if (_boundViewModel?.CurrentMessages is not INotifyCollectionChanged messages)
        {
            return;
        }

        _boundMessages = messages;
        foreach (var message in _boundViewModel.CurrentMessages.OfType<DeviceChatMessageItem>())
        {
            AttachMessageCommands(message);
        }
        _boundMessages.CollectionChanged += OnCurrentMessagesCollectionChanged;
    }

    private void UnbindCurrentMessages()
    {
        if (_boundMessages is null)
        {
            return;
        }

        _boundMessages.CollectionChanged -= OnCurrentMessagesCollectionChanged;
        if (_boundMessages is System.Collections.Generic.IEnumerable<object> messages)
        {
            foreach (var message in messages.OfType<DeviceChatMessageItem>())
            {
                DetachMessageCommands(message);
            }
        }
        _boundMessages = null;
    }

    private void OnCurrentMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is { } oldItems)
        {
            foreach (var message in oldItems.OfType<DeviceChatMessageItem>())
            {
                DetachMessageCommands(message);
            }
        }
        if (e.NewItems is { } items)
        {
            foreach (var message in items.OfType<DeviceChatMessageItem>())
            {
                AttachMessageCommands(message);
            }
        }
        ScrollToLatest();
    }

    private void AttachMessageCommands(DeviceChatMessageItem message)
    {
        message.PreviewImageCommand = _previewImageCommand;
        message.CopyTextCommand = _copyTextCommand;
    }

    private void DetachMessageCommands(DeviceChatMessageItem message)
    {
        if (ReferenceEquals(message.PreviewImageCommand, _previewImageCommand))
        {
            message.PreviewImageCommand = null;
        }

        if (ReferenceEquals(message.CopyTextCommand, _copyTextCommand))
        {
            message.CopyTextCommand = null;
        }
    }

    private void SetConversationItemsSource(System.Collections.IEnumerable? source)
    {
        _conversationItemsControl ??= this.FindControl<ItemsControl>("ConversationItemsControl");
        if (_conversationItemsControl is null)
        {
            return;
        }

        _conversationItemsControl.ItemsSource = source;
    }

    private void RebuildCurrentMessageVisuals()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _conversationItemsControl ??= this.FindControl<ItemsControl>("ConversationItemsControl");
            if (_conversationItemsControl is null || _boundViewModel is null)
            {
                return;
            }

            _conversationItemsControl.ItemsSource = null;
            Dispatcher.UIThread.Post(() =>
            {
                if (_conversationItemsControl is null || _boundViewModel is null)
                {
                    return;
                }

                _conversationItemsControl.ItemsSource = _boundViewModel.CurrentMessages;
                ScrollToLatest();
            }, DispatcherPriority.Render);
        }, DispatcherPriority.Loaded);
    }

    private void ScrollToLatest()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _conversationScrollViewer ??= this.FindControl<ScrollViewer>("ConversationScrollViewer");
            if (_conversationScrollViewer is null)
            {
                return;
            }

            _conversationScrollViewer.ScrollToEnd();

            Dispatcher.UIThread.Post(() =>
            {
                _conversationScrollViewer?.ScrollToEnd();
            }, DispatcherPriority.Render);
        }, DispatcherPriority.Loaded);
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        SetFileDropOverlayVisible(false);
        CloseImagePreview();

        if (_boundViewModel is not null)
        {
            _boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            UnbindCurrentMessages();
            _boundViewModel = null;
        }
    }

    private void OnFileDragOver(object? sender, DragEventArgs e)
    {
        var canSendFiles = _boundViewModel?.CanSendFiles == true && HasLocalFiles(e);
        e.DragEffects = canSendFiles ? DragDropEffects.Copy : DragDropEffects.None;
        SetFileDropOverlayVisible(canSendFiles);
        e.Handled = true;
    }

    private void OnConversationItemFileDragOver(object? sender, DragEventArgs e)
    {
        SetFileDropOverlayVisible(false);

        var hasLocalFiles = HasLocalFiles(e);
        if (_boundViewModel is not null &&
            sender is Border { DataContext: DeviceConversationItem conversation } &&
            hasLocalFiles)
        {
            _boundViewModel.SelectedConversation = conversation;
        }

        e.DragEffects = _boundViewModel?.CanSendFiles == true && hasLocalFiles
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnConversationItemFileDrop(object? sender, DragEventArgs e)
    {
        SetFileDropOverlayVisible(false);

        if (_boundViewModel is null ||
            sender is not Border { DataContext: DeviceConversationItem conversation })
        {
            RejectFileDrop(e);
            return;
        }

        _boundViewModel.SelectedConversation = conversation;
        SendDroppedFiles(_boundViewModel, e);
    }

    private void OnFileDragLeave(object? sender, DragEventArgs e)
    {
        SetFileDropOverlayVisible(false);
    }

    private void OnFileDrop(object? sender, DragEventArgs e)
    {
        SetFileDropOverlayVisible(false);

        var viewModel = _boundViewModel;
        if (viewModel?.CanSendFiles != true)
        {
            RejectFileDrop(e);
            return;
        }

        SendDroppedFiles(viewModel, e);
    }

    private static void SendDroppedFiles(DeviceCommunicationPageViewModel viewModel, DragEventArgs e)
    {
        var filePaths = GetLocalFilePaths(e);

        if (viewModel.CanSendFiles && filePaths.Length > 0)
        {
            viewModel.SendFiles(filePaths);
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private static string[] GetLocalFilePaths(DragEventArgs e)
    {
        return e.DataTransfer.TryGetFiles()?
            .OfType<IStorageFile>()
            .Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
    }

    private static void RejectFileDrop(DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.None;
        e.Handled = true;
    }

    private static bool HasLocalFiles(DragEventArgs e)
    {
        return e.DataTransfer.Contains(DataFormat.File) && GetLocalFilePaths(e).Length > 0;
    }

    private void SetFileDropOverlayVisible(bool isVisible)
    {
        _fileDropOverlay ??= this.FindControl<Border>("FileDropOverlay");
        if (_fileDropOverlay is not null)
        {
            _fileDropOverlay.IsVisible = isVisible;
        }
    }

    private void OnFileCardDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (_boundViewModel?.OpenFileCommand is null) return;
        if (e.Source is not Control { DataContext: FileChatMessageItem item } || !item.CanUseLocalFile) return;
        _boundViewModel.OpenFileCommand.Execute(item);
    }

    private void OnMessagePointerPressedTunnel(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            _pendingContextSelection = null;
        }
    }

    private void OnMessageContextRequestedTunnel(object? sender, ContextRequestedEventArgs e)
    {
        var textBlock = FindAncestorMessageTextBlock(e.Source as Visual);
        _pendingContextSelection = textBlock is not null && !string.IsNullOrEmpty(textBlock.SelectedText)
            ? (new WeakReference<SelectableTextBlock>(textBlock), textBlock.SelectedText)
            : null;
    }

    private void OnMessageContextRequestedBubble(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Handled || e.Source is not Visual source)
        {
            return;
        }

        for (Visual? current = source; current is not null && !ReferenceEquals(current, this); current = current.GetVisualParent())
        {
            if (current is Image or SelectableTextBlock)
            {
                return;
            }

            if (current is Border { DataContext: DeviceChatMessageItem { HasText: true } } bubble)
            {
                var textBlock = bubble.GetVisualDescendants()
                    .OfType<SelectableTextBlock>()
                    .FirstOrDefault(candidate => candidate.IsVisible);
                if (textBlock?.ContextFlyout is MenuFlyout flyout)
                {
                    _pendingContextSelection = !string.IsNullOrEmpty(textBlock.SelectedText)
                        ? (new WeakReference<SelectableTextBlock>(textBlock), textBlock.SelectedText)
                        : null;
                    flyout.ShowAt(textBlock, showAtPointer: true);
                    e.Handled = true;
                }

                return;
            }
        }
    }

    private void OnSelectableTextBlockCopyingToClipboard(object? sender, RoutedEventArgs e)
    {
        if (_boundViewModel?.HasClipboardService != true ||
            e.Source is not SelectableTextBlock textBlock ||
            string.IsNullOrEmpty(textBlock.SelectedText))
        {
            return;
        }

        e.Handled = true;
        _ = CopyTextToClipboardAsync(textBlock.SelectedText);
    }

    private SelectableTextBlock? FindAncestorMessageTextBlock(Visual? source)
    {
        for (Visual? current = source; current is not null && !ReferenceEquals(current, this); current = current.GetVisualParent())
        {
            if (current is SelectableTextBlock { DataContext: DeviceChatMessageItem } textBlock)
            {
                return textBlock;
            }
        }

        return null;
    }

    private static bool CanCopyMessageText(SelectableTextBlock? textBlock)
    {
        if (textBlock is null)
        {
            return false;
        }

        return textBlock.DataContext is DeviceChatMessageItem item
            ? item.HasText
            : !string.IsNullOrEmpty(textBlock.Text);
    }

    private async Task CopyMessageTextAsync(SelectableTextBlock? textBlock)
    {
        if (textBlock is null)
        {
            return;
        }

        string? textToCopy = null;
        if (!string.IsNullOrEmpty(textBlock.SelectedText))
        {
            textToCopy = textBlock.SelectedText;
        }
        else if (_pendingContextSelection is { } pending &&
                 pending.Target.TryGetTarget(out var target) &&
                 ReferenceEquals(target, textBlock))
        {
            textToCopy = pending.SelectedText;
        }

        _pendingContextSelection = null;

        if (string.IsNullOrEmpty(textToCopy))
        {
            textToCopy = textBlock.DataContext is DeviceChatMessageItem item
                ? item.Text
                : textBlock.Text;
        }

        await CopyTextToClipboardAsync(textToCopy);
    }

    private async Task CopyTextToClipboardAsync(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (_boundViewModel is not null && await _boundViewModel.CopyTextAsync(text))
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        try
        {
            await clipboard.SetTextAsync(text);
        }
        catch (Exception)
        {
            // Clipboard access can fail transiently when another process holds the clipboard.
        }
    }

    private void OnPreviewImagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Image { DataContext: DeviceChatMessageItem messageItem })
        {
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            ShowImagePreview(messageItem);
            e.Handled = true;
        }
    }

    private void ShowImagePreview(DeviceChatMessageItem? messageItem)
    {
        if (messageItem?.ImagePreview is null ||
            _imagePreviewOverlay is null ||
            _imagePreviewCanvas is null ||
            _imagePreviewContent is null)
        {
            return;
        }

        _imagePreviewContent.Source = messageItem.ImagePreview;
        _imagePreviewContent.Cursor = new Cursor(StandardCursorType.Hand);
        _imagePreviewWidth = Math.Max(1d, messageItem.ImagePreview.PixelSize.Width);
        _imagePreviewHeight = Math.Max(1d, messageItem.ImagePreview.PixelSize.Height);
        _imagePreviewScale = 1d;
        _imagePreviewMinScale = 1d;
        _imagePreviewOffsetX = 0d;
        _imagePreviewOffsetY = 0d;
        _imagePreviewOverlay.IsVisible = true;
        _imagePreviewOverlay.Focus();

        Dispatcher.UIThread.Post(
            () => RecalculateImagePreviewScale(resetZoom: true),
            DispatcherPriority.Loaded);
    }

    private void OnImagePreviewCloseClicked(object? sender, RoutedEventArgs e)
    {
        CloseImagePreview();
        e.Handled = true;
    }

    private void OnImagePreviewZoomOutClicked(object? sender, RoutedEventArgs e)
    {
        ZoomImagePreviewAroundViewportCenter(0.8d);
        e.Handled = true;
    }

    private void OnImagePreviewResetZoomClicked(object? sender, RoutedEventArgs e)
    {
        RecalculateImagePreviewScale(resetZoom: true);
        e.Handled = true;
    }

    private void OnImagePreviewZoomInClicked(object? sender, RoutedEventArgs e)
    {
        ZoomImagePreviewAroundViewportCenter(1.25d);
        e.Handled = true;
    }

    private void OnImagePreviewOverlayPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(e.Source, sender))
        {
            CloseImagePreview();
            e.Handled = true;
        }
    }

    private void OnImagePreviewOverlayKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        CloseImagePreview();
        e.Handled = true;
    }

    private void OnImagePreviewCanvasSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_imagePreviewOverlay?.IsVisible == true)
        {
            RecalculateImagePreviewScale(resetZoom: false);
        }
    }

    private void OnImagePreviewPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_imagePreviewCanvas is null || _imagePreviewOverlay?.IsVisible != true)
        {
            return;
        }

        var wheelDelta = e.Delta.Y;
        if (Math.Abs(wheelDelta) <= double.Epsilon)
        {
            return;
        }

        var pointer = e.GetPosition(_imagePreviewCanvas);
        e.Handled = TryZoomImagePreview(wheelDelta > 0 ? 1.1d : 0.9d, pointer);
    }

    private void OnImagePreviewContentPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_imagePreviewCanvas is null ||
            _imagePreviewContent is null ||
            !e.GetCurrentPoint(_imagePreviewContent).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _isImagePreviewPanning = true;
        _imagePreviewPanStartPoint = e.GetPosition(_imagePreviewCanvas);
        _imagePreviewPanStartOffsetX = _imagePreviewOffsetX;
        _imagePreviewPanStartOffsetY = _imagePreviewOffsetY;
        _imagePreviewContent.Cursor = new Cursor(StandardCursorType.SizeAll);
        _imagePreviewCapturedPointer = e.Pointer;
        e.Pointer.Capture(_imagePreviewContent);
        e.Handled = true;
    }

    private void OnImagePreviewContentPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isImagePreviewPanning || _imagePreviewCanvas is null)
        {
            return;
        }

        var currentPoint = e.GetPosition(_imagePreviewCanvas);
        var delta = currentPoint - _imagePreviewPanStartPoint;
        _imagePreviewOffsetX = _imagePreviewPanStartOffsetX + delta.X;
        _imagePreviewOffsetY = _imagePreviewPanStartOffsetY + delta.Y;
        ApplyImagePreviewLayout();
        e.Handled = true;
    }

    private void OnImagePreviewContentPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isImagePreviewPanning)
        {
            return;
        }

        EndImagePreviewPan();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnImagePreviewContentPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_isImagePreviewPanning)
        {
            return;
        }

        EndImagePreviewPan();
        e.Handled = true;
    }

    private void RecalculateImagePreviewScale(bool resetZoom)
    {
        if (_imagePreviewCanvas is null)
        {
            return;
        }

        var viewportWidth = _imagePreviewCanvas.Bounds.Width;
        var viewportHeight = _imagePreviewCanvas.Bounds.Height;
        if (viewportWidth <= 0 || viewportHeight <= 0)
        {
            return;
        }

        var fitScale = Math.Min(
            viewportWidth / _imagePreviewWidth,
            viewportHeight / _imagePreviewHeight);
        if (fitScale <= 0 || double.IsNaN(fitScale) || double.IsInfinity(fitScale))
        {
            return;
        }

        _imagePreviewMinScale = fitScale;
        if (resetZoom || _imagePreviewScale < _imagePreviewMinScale)
        {
            _imagePreviewScale = _imagePreviewMinScale;
        }

        ApplyImagePreviewLayout();
        UpdateImagePreviewTitle();
    }

    private void ZoomImagePreviewAroundViewportCenter(double factor)
    {
        if (_imagePreviewCanvas is null)
        {
            return;
        }

        var center = new Point(
            _imagePreviewCanvas.Bounds.Width / 2d,
            _imagePreviewCanvas.Bounds.Height / 2d);
        TryZoomImagePreview(factor, center);
    }

    private bool TryZoomImagePreview(double factor, Point anchor)
    {
        if (_imagePreviewOverlay?.IsVisible != true || _imagePreviewMinScale <= 0)
        {
            return false;
        }

        var maxScale = _imagePreviewMinScale * 8d;
        var newScale = Math.Clamp(
            _imagePreviewScale * factor,
            _imagePreviewMinScale,
            maxScale);
        if (Math.Abs(newScale - _imagePreviewScale) <= double.Epsilon)
        {
            return false;
        }

        var relativeX = (anchor.X - _imagePreviewOffsetX) / _imagePreviewScale;
        var relativeY = (anchor.Y - _imagePreviewOffsetY) / _imagePreviewScale;

        _imagePreviewScale = newScale;
        _imagePreviewOffsetX = anchor.X - relativeX * _imagePreviewScale;
        _imagePreviewOffsetY = anchor.Y - relativeY * _imagePreviewScale;

        ApplyImagePreviewLayout();
        UpdateImagePreviewTitle();
        return true;
    }

    private void ApplyImagePreviewLayout()
    {
        if (_imagePreviewCanvas is null || _imagePreviewContent is null)
        {
            return;
        }

        var viewportWidth = _imagePreviewCanvas.Bounds.Width;
        var viewportHeight = _imagePreviewCanvas.Bounds.Height;
        if (viewportWidth <= 0 || viewportHeight <= 0)
        {
            return;
        }

        var scaledWidth = _imagePreviewWidth * _imagePreviewScale;
        var scaledHeight = _imagePreviewHeight * _imagePreviewScale;

        _imagePreviewOffsetX = scaledWidth <= viewportWidth
            ? (viewportWidth - scaledWidth) / 2d
            : Math.Clamp(_imagePreviewOffsetX, viewportWidth - scaledWidth, 0d);
        _imagePreviewOffsetY = scaledHeight <= viewportHeight
            ? (viewportHeight - scaledHeight) / 2d
            : Math.Clamp(_imagePreviewOffsetY, viewportHeight - scaledHeight, 0d);

        _imagePreviewContent.Width = scaledWidth;
        _imagePreviewContent.Height = scaledHeight;
        Canvas.SetLeft(_imagePreviewContent, _imagePreviewOffsetX);
        Canvas.SetTop(_imagePreviewContent, _imagePreviewOffsetY);
    }

    private void UpdateImagePreviewTitle()
    {
        if (_imagePreviewTitle is null || _imagePreviewMinScale <= 0)
        {
            return;
        }

        var zoomPercent = _imagePreviewScale / _imagePreviewMinScale * 100d;
        _imagePreviewTitle.Text = Lang.Format("lang.kitopia.messages.image_preview_value", zoomPercent);
    }

    private void EndImagePreviewPan()
    {
        _isImagePreviewPanning = false;
        _imagePreviewCapturedPointer = null;
        if (_imagePreviewContent is not null)
        {
            _imagePreviewContent.Cursor = new Cursor(StandardCursorType.Hand);
        }
    }

    private void CloseImagePreview()
    {
        _imagePreviewCapturedPointer?.Capture(null);
        EndImagePreviewPan();

        if (_imagePreviewContent is not null)
        {
            _imagePreviewContent.Source = null;
        }

        if (_imagePreviewOverlay is not null)
        {
            _imagePreviewOverlay.IsVisible = false;
        }
    }
}
