#region

using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Core.SDKs.HotKey;
using Core.SDKs.Services;
using Core.SDKs.Services.Config;
using log4net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using PluginCore;
using PluginCore.Config;

#endregion

namespace Core.ViewModel.Pages;

public partial class SettingPageViewModel : ObservableRecipient
{
    private static readonly ILog log = LogManager.GetLogger("SettingPageViewModel");

    private ConfigBase _configBase;


    public SettingPageViewModel(ConfigBase configBase)
    {
        _configBase = configBase;
    }
}