using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Core.SDKs.HotKey;
using Core.SDKs.Services.Config;
using PluginCore;

namespace Core.ViewModel.Pages;

public partial class HotKeyManagerPageViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<HotKeyModel> _keyModels;
    public HotKeyManagerPageViewModel()
    {
        KeyModels = new ObservableCollection<HotKeyModel>(HotKeyManager.HotKetImpl.GetAllRegistered());
        WeakReferenceMessenger.Default.Register<string, string>(this, "hotkey", (recipient, message) =>
        {
            KeyModels = new ObservableCollection<HotKeyModel>(HotKeyManager.HotKetImpl.GetAllRegistered());
        });
    }
}