using CommunityToolkit.Mvvm.ComponentModel;
using Core.SDKs.Services.Config;
using Core.SDKs.Services.Plugin;
using PluginCore.Onnx;

namespace Core.ViewModel.Pages;

public partial class OnnxModelRuntimeChangerHelper : ObservableObject
{
   public string TargetDevice { get; set; }
   [ObservableProperty]
   public string currentDevice;

   partial void OnCurrentDeviceChanged(string value)
   {
      if (OnnxModelInfoWrapper is null)
      {
         return;
      }
      ConfigManger.Config.OnnxTargetDevices[OnnxModelInfoWrapper.Model.SignName] = value;
      ConfigManger.Save("KitopiaConfig");
   }

   public OnnxModelInfoWrapper OnnxModelInfoWrapper { get; set; }
}
public partial class OnnxModelManagerPageViewModel : ObservableObject
{
  

}