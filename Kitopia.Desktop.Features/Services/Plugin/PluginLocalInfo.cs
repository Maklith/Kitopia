using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using Kitopia.Desktop.Features.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Kitopia.Desktop.Features.Services.Plugin;

public partial class PluginLocalInfo : ObservableObject
{
    public PluginBaseInfo PluginBaseInfo { set; get; }
    [JsonIgnore] public string FullPath { set; get; }
    [JsonIgnore] public string Path { set; get; }
    public bool IsEnabled => ServiceManager.Services is { } services &&
                             services.GetService<ICustomScenarioPluginIntegration>()?
                                 .IsPluginEnabled(PluginBaseInfo.NameSign) == true;
    [JsonIgnore] [ObservableProperty] public bool unloadFailed;
    [JsonIgnore] [ObservableProperty] public bool loadFailed;
    [JsonIgnore] [ObservableProperty] public string? loadFailedReason;

    public void NotifyStatusChanged()
    {
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(LoadFailed));
        OnPropertyChanged(nameof(LoadFailedReason));
    }

    public string ToPlgString()
    {
        return PluginBaseInfo.ToPlgString();
    }

    public override string ToString()
    {
        return ToPlgString();
    }
}
