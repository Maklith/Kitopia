using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Kitopia.Desktop.Features.Services.HotKey;
using Kitopia.Desktop.Features.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Kitopia.Desktop.Features.ViewModel.Pages;

public partial class HotKeyManagerPageViewModel : ObservableObject
{
    public ObservableCollection<HotKeyModel> KeyModels { get; }

    public HotKeyManagerPageViewModel()
    {
        var hotkeys = ServiceManager.Services.GetRequiredService<IHotKetImpl>();
        KeyModels = new ObservableCollection<HotKeyModel>(hotkeys.GetAllRegistered());
        WeakReferenceMessenger.Default.Register<HotKeyChanged>(this, static (recipient, message) =>
        {
            var viewModel = (HotKeyManagerPageViewModel)recipient;
            if (Dispatcher.UIThread.CheckAccess()) viewModel.UpdateHotKey(message);
            else Dispatcher.UIThread.Post(() => viewModel.UpdateHotKey(message));
        });
    }

    private void UpdateHotKey(HotKeyChanged message)
    {
        var existing = KeyModels.FirstOrDefault(model => model.UUID == message.Uuid);
        var current = ServiceManager.Services.GetRequiredService<IHotKetImpl>().GetByUuid(message.Uuid);
        if (current is null)
        {
            if (existing is not null) KeyModels.Remove(existing);
        }
        else if (existing is null)
            KeyModels.Add(current);
        else if (!ReferenceEquals(existing, current))
            KeyModels[KeyModels.IndexOf(existing)] = current;
    }
}
