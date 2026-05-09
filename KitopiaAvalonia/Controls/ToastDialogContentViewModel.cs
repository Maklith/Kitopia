using System;
using System.Collections.ObjectModel;
using System.Threading;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Irihi.Avalonia.Shared.Contracts;
using PluginCore;

namespace KitopiaAvalonia.Controls;

public sealed class ToastDialogContentViewModel : ObservableObject, IDialogContext
{
    private int _isClosed;

    public void Close()
    {
        InvokeCloseActionOnce();
        RequestClose?.Invoke(this, null);
    }

    public event EventHandler<object?>? RequestClose;
    private readonly Action? _closeAction;

    public ToastDialogContentViewModel(ToastRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        Header = request.Header;
        Text = request.Text;
        NotificationType = request.NotificationType;
        ShowCloseButton = request.ShowCloseButton;
        ShowProgressBar = request.ShowProgressBar;
        IsProgressIndeterminate = request.IsProgressIndeterminate;
        ProgressValue = request.ProgressValue;
        _closeAction = request.CloseAction;
        foreach (var action in request.Actions)
        {
            var callback = action.Callback;
            
            Actions.Add(new ToastDialogActionViewModel(action.Text, action.IsPrimary, () =>
            {
                callback?.Invoke();
                if (action.ShouldCloseOnClick)
                {
                    Close();
                }
            }));
        }
    }

    public string Header { get; }

    public string Text { get; }

    public NotificationType NotificationType { get; }

    public bool ShowCloseButton { get; }

    public bool ShowProgressBar { get; }

    public bool IsProgressIndeterminate { get; }

    public double? ProgressValue { get; }

    public ObservableCollection<ToastDialogActionViewModel> Actions { get; } = [];

    public bool HasActions => Actions.Count > 0;

    public bool IsInformation => NotificationType == NotificationType.Information;

    public bool IsSuccess => NotificationType == NotificationType.Success;

    public bool IsWarning => NotificationType == NotificationType.Warning;

    public bool IsError => NotificationType == NotificationType.Error;

    public void EnsureClosed()
    {
        InvokeCloseActionOnce();
    }

    private void InvokeCloseActionOnce()
    {
        if (Interlocked.Exchange(ref _isClosed, 1) == 1)
        {
            return;
        }

        _closeAction?.Invoke();
    }
}

public sealed class ToastDialogActionViewModel
{
    public ToastDialogActionViewModel(string text, bool isPrimary, Action execute)
    {
        Text = text;
        IsPrimary = isPrimary;
        Command = new RelayCommand(execute);
    }

    public string Text { get; }

    public bool IsPrimary { get; }

    public IRelayCommand Command { get; }
}
