using Core.Services;
using Core.ViewModel;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using PluginCore;
using PluginCore.SearchWindow.InputDataAnalyzer;
using Serilog;

namespace Core.UI.SearchWindow.InputData;

public class ImageIdentifier : IInputDataIdentifier
{
    private static ILogger Logger = LogManager.Logger.ForContext<ImageIdentifier>();

    public IEnumerable<PluginCore.SearchWindow.InputData.InputData> IdentifyInputData(
        InputDataAnalyzeTimeFlags analyzeTimeFlags, string? s)
    {
        if (ServiceManager.Services.GetService<IClipboardService>()!.HasImage())
        {
            Logger.Debug("剪贴板有图像信息");
            var image = ServiceManager.Services.GetService<IClipboardService>()!.GetImage();
            var identifyInputData = new PluginCore.SearchWindow.InputData.InputData
            {
                InputType = InputType.图像,
                Data = image,
                DisposeAction = (e) =>
                {
                    var objData = e.Data as Mat;
                    objData.Dispose();
                }
            };
            yield return identifyInputData;
            
        }

        yield break;
    }
}