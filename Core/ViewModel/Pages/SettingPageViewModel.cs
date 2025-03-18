#region

using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Core.SDKs.HotKey;
using Core.SDKs.Services;
using Core.SDKs.Services.Config;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using PluginCore;
using PluginCore.Config;

#endregion

namespace Core.ViewModel.Pages;

public partial class SettingPageViewModel : ObservableRecipient
{
    

    private ConfigBase _configBase;


    public SettingPageViewModel(ConfigBase configBase)
    {
        _configBase = configBase;
    }
}