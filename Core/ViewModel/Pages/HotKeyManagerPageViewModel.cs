using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Core.Services.HotKey;
using Core.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Core.ViewModel.Pages;

public partial class HotKeyManagerPageViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<HotKeyModel> _keyModels;

    public HotKeyManagerPageViewModel()
    {
        KeyModels = new ObservableCollection<HotKeyModel>(ServiceManager.Services.GetService<IHotKetImpl>()!.GetAllRegistered());
        WeakReferenceMessenger.Default.Register<string, string>(this, "hotkey",
            (_, _) =>
            {
                
                KeyModels = new ObservableCollection<HotKeyModel>(ServiceManager.Services.GetService<IHotKetImpl>()!.GetAllRegistered());
            });
    }
}