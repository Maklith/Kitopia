using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Core.ViewModel.Pages.device;

namespace KitopiaAvalonia.Pages;

public partial class DeviceChatPage : UserControl
{
    private DeviceChatPageViewModel? _viewModel;

    public DeviceChatPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        PropertyChanged += OnControlPropertyChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.Messages.CollectionChanged -= OnMessagesCollectionChanged;
        }

        _viewModel = DataContext as DeviceChatPageViewModel;
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.Messages.CollectionChanged += OnMessagesCollectionChanged;
        ScheduleScrollToBottom();
    }

    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            ScheduleScrollToBottom();
            return;
        }

        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems is null || e.NewItems.Count <= 0)
        {
            return;
        }

        var totalCount = _viewModel?.Messages.Count ?? 0;
        var insertedAtEnd = e.NewStartingIndex >= 0 && e.NewStartingIndex + e.NewItems.Count >= totalCount;
        if (insertedAtEnd)
        {
            ScheduleScrollToBottom();
        }
    }

    private void OnControlPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty && e.NewValue is bool isVisible && isVisible)
        {
            ScheduleScrollToBottom();
        }
    }

    private void ScheduleScrollToBottom()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var scrollViewer = MessagesListBox
                .GetVisualDescendants()
                .OfType<ScrollViewer>()
                .FirstOrDefault();
            if (scrollViewer is null)
            {
                return;
            }

            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, scrollViewer.Extent.Height);
        }, DispatcherPriority.Background);
    }
}
