using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Kitopia.Desktop.Services;

internal sealed class SuppressedNotificationCenterViewModel : ObservableObject
{
    public SuppressedNotificationCenterViewModel(Action clearAllAction)
    {
        ClearAllCommand = new RelayCommand(clearAllAction, () => HasNotifications);
        Items.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasNotifications));
            ClearAllCommand.NotifyCanExecuteChanged();
        };
    }

    public ObservableCollection<ToastItemViewModel> Items { get; } = [];
    public bool HasNotifications => Items.Count > 0;
    public IRelayCommand ClearAllCommand { get; }
}
