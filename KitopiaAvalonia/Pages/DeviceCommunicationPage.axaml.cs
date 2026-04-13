using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using Core.ViewModel.Pages.device;

namespace KitopiaAvalonia.Pages;

public partial class DeviceCommunicationPage : UserControl
{
    private DeviceCommunicationPageViewModel? _boundViewModel;

    public DeviceCommunicationPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_boundViewModel is not null)
        {
            _boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _boundViewModel = null;
        }

        _boundViewModel = DataContext as DeviceCommunicationPageViewModel;
        if (_boundViewModel is not null)
        {
            _boundViewModel.PropertyChanged += OnViewModelPropertyChanged;
            ScrollToLatest();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DeviceCommunicationPageViewModel.MessageListVersion) or nameof(DeviceCommunicationPageViewModel.CurrentMessages))
        {
            ScrollToLatest();
        }
    }

    private void ScrollToLatest()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var scrollViewer = this.FindControl<ScrollViewer>("ConversationScrollViewer") ?? this.FindDescendantOfType<ScrollViewer>();
            scrollViewer?.ScrollToEnd();
        }, DispatcherPriority.Background);
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (_boundViewModel is not null)
        {
            _boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _boundViewModel = null;
        }
    }
}
