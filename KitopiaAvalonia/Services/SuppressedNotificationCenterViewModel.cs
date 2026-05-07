using System.Collections.ObjectModel;

namespace KitopiaAvalonia.Services;

internal sealed class SuppressedNotificationCenterViewModel
{
    public ObservableCollection<SuppressedNotificationItemViewModel> Items { get; } = [];
}
