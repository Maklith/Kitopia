using System.Text.Json.Serialization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using PluginCore;

namespace Core.SDKs.Services.Plugin;

public partial class PluginLocalInfo: ObservableObject
{
    public PluginBaseInfo PluginBaseInfo { set; get; }
    [JsonIgnore]public string FullPath { set; get; }
    [JsonIgnore]public string Path { set; get; }
    public bool IsEnabled => PluginManager.GetPluginLocalInfoOnlyOnEnableByPlgStr(PluginBaseInfo.NameSign)is not null;
    [JsonIgnore][ObservableProperty] public bool unloadFailed;

    public void NotifyStatusChanged()
    {
        OnPropertyChanged(nameof(IsEnabled));
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