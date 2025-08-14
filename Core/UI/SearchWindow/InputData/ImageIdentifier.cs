using Core.SDKs.Services;
using Core.ViewModel;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using PluginCore;
using PluginCore.SearchWindow.InputDataAnalyzer;
using Serilog;

namespace Core.SearchWindow.InputData;

public class ImageIdentifier : IInputDataIdentifier
{
    private static ILogger Log =   LogManager.Logger.ForContext<ImageIdentifier>();
    public IEnumerable<PluginCore.SearchWindow.InputData.InputData> IdentifyInputData(IInputDataAnalyzeTimeFlags analyzeTimeFlags,string? s)
    {
        if (ServiceManager.Services.GetService<IClipboardService>()!.HasImage())
        {
            Log.Debug("剪贴板有图像信息");
            var image = ServiceManager.Services.GetService<IClipboardService>()!.GetImage();
            var identifyInputData = new PluginCore.SearchWindow.InputData.InputData()
            {
                InputType = InputType.图像,
                Data = image,
                DisposeAction = ((e) =>
                {
                    var objData = e.Data as Mat;
                    objData.Dispose();
                })
            };
            yield return identifyInputData;
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