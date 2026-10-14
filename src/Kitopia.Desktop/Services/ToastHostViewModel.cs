using System.Collections.ObjectModel;

namespace Kitopia.Desktop.Services;

internal sealed class ToastHostViewModel
{
    public ObservableCollection<ToastItemViewModel> Items { get; } = [];
}
