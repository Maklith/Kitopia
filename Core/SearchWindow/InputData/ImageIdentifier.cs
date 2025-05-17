using Core.SDKs.Services;
using Core.ViewModel;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using Serilog;

namespace Core.SearchWindow.InputData;

public class ImageIdentifier : IInputDataIdentifier
{
    private static ILogger Log =   LogManager.Logger.ForContext<ImageIdentifier>();
    public IEnumerable<ViewModel.InputData> IdentifyInputData(string? s)
    {
        if (ServiceManager.Services.GetService<IClipboardService>()!.HasImage())
        {
            Log.Debug("剪贴板有图像信息");
            
            // Items.Insert(0, new SearchViewItem()
            // {
            //     ItemDisplayName = "保存剪贴板图像?",
            //     FileType = FileType.剪贴板图像,
            //     IconSymbol = 0xE357,
            //     OnlyKey = "ClipboardImageData",
            //     Icon = null,
            //     IsVisible = true
            // });
        }
        yield break;
    }
}