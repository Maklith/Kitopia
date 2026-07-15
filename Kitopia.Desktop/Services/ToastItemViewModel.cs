using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PluginCore;

namespace Kitopia.Desktop.Services;

internal sealed partial class ToastItemViewModel : ObservableObject
{
    [ObservableProperty] private string _header;
    [ObservableProperty] private string _text;
    private NotificationType _notificationType;
    [ObservableProperty] private bool _isClosing;
    [ObservableProperty] private bool _showProgressBar;
    [ObservableProperty] private bool _isProgressIndeterminate;
    [ObservableProperty] private double? _progressValue;
    [ObservableProperty] private bool _showCloseButton;

    public ToastItemViewModel(Guid id, ToastRequest request, Action closeAction,
        Action? clickAction = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        Id = id;
        _header = request.Header;
        _text = request.Text;
        _notificationType = request.NotificationType;
        _showCloseButton = request.ShowCloseButton;
        _showProgressBar = request.ShowProgressBar;
        _isProgressIndeterminate = request.IsProgressIndeterminate;
        _progressValue = request.ProgressValue;
        CloseCommand = new RelayCommand(closeAction);
        ClickCommand = clickAction is null ? null : new RelayCommand(clickAction);
        Actions.CollectionChanged += OnActionsCollectionChanged;
    }

    public Guid Id { get; }

    public ObservableCollection<ToastActionViewModel> Actions { get; } = [];

    public bool HasActions => Actions.Count > 0;

    public IRelayCommand CloseCommand { get; }

    public IRelayCommand? ClickCommand { get; }

    public NotificationType NotificationType
    {
        get => _notificationType;
        set
        {
            if (!SetProperty(ref _notificationType, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsInformation));
            OnPropertyChanged(nameof(IsSuccess));
            OnPropertyChanged(nameof(IsWarning));
            OnPropertyChanged(nameof(IsError));
        }
    }

    public bool IsInformation => NotificationType == NotificationType.Information;

    public bool IsSuccess => NotificationType == NotificationType.Success;

    public bool IsWarning => NotificationType == NotificationType.Warning;

    public bool IsError => NotificationType == NotificationType.Error;
    
    private void OnActionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasActions));
    }
}
