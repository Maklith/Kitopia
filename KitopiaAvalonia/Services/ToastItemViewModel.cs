using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace KitopiaAvalonia.Services;

internal sealed class ToastItemViewModel : ObservableObject
{
    private string _header;
    private string _text;
    private NotificationType _notificationType;
    private bool _isClosing;
    private bool _showProgressBar;
    private bool _isProgressIndeterminate;
    private double? _progressValue;
    private bool _showCloseButton;

    public ToastItemViewModel(Guid id, string header, string text, NotificationType notificationType, bool showCloseButton,
        bool showProgressBar, bool isProgressIndeterminate, double? progressValue, Action closeAction,
        Action? clickAction = null)
    {
        Id = id;
        _header = header;
        _text = text;
        _notificationType = notificationType;
        _showCloseButton = showCloseButton;
        _showProgressBar = showProgressBar;
        _isProgressIndeterminate = isProgressIndeterminate;
        _progressValue = progressValue;
        CloseCommand = new RelayCommand(closeAction);
        ClickCommand = clickAction is null ? null : new RelayCommand(clickAction);
        Actions.CollectionChanged += OnActionsCollectionChanged;
    }

    public Guid Id { get; }

    public ObservableCollection<ToastActionViewModel> Actions { get; } = [];

    public bool HasActions => Actions.Count > 0;

    public IRelayCommand CloseCommand { get; }

    public IRelayCommand? ClickCommand { get; }

    public string Header
    {
        get => _header;
        set => SetProperty(ref _header, value);
    }

    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    public NotificationType NotificationType
    {
        get => _notificationType;
        set
        {
            if (!SetProperty(ref _notificationType, value))
            {
                return;
            }

            OnNotificationTypeChanged();
        }
    }

    public bool IsClosing
    {
        get => _isClosing;
        set => SetProperty(ref _isClosing, value);
    }

    public bool ShowProgressBar
    {
        get => _showProgressBar;
        set => SetProperty(ref _showProgressBar, value);
    }

    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        set => SetProperty(ref _isProgressIndeterminate, value);
    }

    public double? ProgressValue
    {
        get => _progressValue;
        set => SetProperty(ref _progressValue, value);
    }

    public bool ShowCloseButton
    {
        get => _showCloseButton;
        set => SetProperty(ref _showCloseButton, value);
    }

    public bool IsInformation => NotificationType == NotificationType.Information;

    public bool IsSuccess => NotificationType == NotificationType.Success;

    public bool IsWarning => NotificationType == NotificationType.Warning;

    public bool IsError => NotificationType == NotificationType.Error;

    private void OnActionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasActions));
    }

    private void OnNotificationTypeChanged()
    {
        OnPropertyChanged(nameof(IsInformation));
        OnPropertyChanged(nameof(IsSuccess));
        OnPropertyChanged(nameof(IsWarning));
        OnPropertyChanged(nameof(IsError));
    }
}
