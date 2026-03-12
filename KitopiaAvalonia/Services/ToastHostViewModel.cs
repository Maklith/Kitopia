using System.Collections.ObjectModel;

namespace KitopiaAvalonia.Services;

internal sealed class ToastHostViewModel
{
    public ObservableCollection<ToastItemViewModel> Items { get; } = [];
}
