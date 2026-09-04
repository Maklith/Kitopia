using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Irihi.Avalonia.Shared.Contracts;
using PluginCore;

namespace Kitopia.Desktop.Controls;

public sealed class ToastDialogContentViewModel : ObservableObject, IDialogContext
{
    private readonly Action<string>? _selectionConfirmed;
    private readonly bool? _showIcon;
    private string? _selectedOption;

    public void Close()
    {
        RequestClose?.Invoke(this, null);
    }
    public event EventHandler<object?>? RequestClose;
    public ToastDialogContentViewModel(ToastRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        Header = request.Header;
        Text = request.Text;
        NotificationType = request.NotificationType;
        _showIcon = request.ShowIcon;
        ShowCloseButton = request.ShowCloseButton;
        ShowProgressBar = request.ShowProgressBar;
        IsProgressIndeterminate = request.IsProgressIndeterminate;
        ProgressValue = request.ProgressValue;
        SelectionOptions = request.SelectionOptions;
        SelectedOption = request.SelectedOption is { } selected && request.SelectionOptions.Contains(selected)
            ? selected
            : request.SelectionOptions.FirstOrDefault();
        SelectionConfirmText = request.SelectionConfirmText;
        _selectionConfirmed = request.SelectionConfirmed;
        ConfirmSelectionCommand = new RelayCommand(ConfirmSelection);
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

    public bool HasHeader => !string.IsNullOrWhiteSpace(Header);

    public bool HasIcon => _showIcon ?? (NotificationType switch
    {
        NotificationType.Warning => true,
        NotificationType.Error => true,
        NotificationType.Success => true,
        _ => Actions.Count == 0 && HasHeader
    });

    public Thickness ContentMargin => HasHeader ? new Thickness(0, 12, 0, 0) : new Thickness(0);

    public bool ShowCloseButton { get; }

    public bool ShowProgressBar { get; }

    public bool IsProgressIndeterminate { get; }

    public double? ProgressValue { get; }

    public IReadOnlyList<string> SelectionOptions { get; }

    public string? SelectedOption
    {
        get => _selectedOption;
        set => SetProperty(ref _selectedOption, value);
    }

    public string? SelectionConfirmText { get; }

    public bool HasSelectionOptions => SelectionOptions.Count > 0;

    public bool HasSelectionConfirmation => HasSelectionOptions &&
                                            !string.IsNullOrWhiteSpace(SelectionConfirmText) &&
                                            _selectionConfirmed is not null;

    public IRelayCommand ConfirmSelectionCommand { get; }

    public ObservableCollection<ToastDialogActionViewModel> Actions { get; } = [];

    public bool HasActions => Actions.Count > 0;

    public bool HasFooter => HasSelectionConfirmation || HasActions;

    public bool IsInformation => NotificationType == NotificationType.Information;

    public bool IsSuccess => NotificationType == NotificationType.Success;

    public bool IsWarning => NotificationType == NotificationType.Warning;

    public bool IsError => NotificationType == NotificationType.Error;

    private void ConfirmSelection()
    {
        if (SelectedOption is null)
        {
            return;
        }

        _selectionConfirmed?.Invoke(SelectedOption);
        Close();
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
