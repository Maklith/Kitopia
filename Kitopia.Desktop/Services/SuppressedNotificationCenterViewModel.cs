using System.Collections.ObjectModel;

namespace Kitopia.Desktop.Services;

internal sealed class SuppressedNotificationCenterViewModel
{
    public ObservableCollection<SuppressedNotificationItemViewModel> Items { get; } = [];
}
