using System.Collections.Generic;
using Kitopia.Desktop.Features.Services;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using PluginCore;
using PluginCore.SearchWindow.InputData;
using PluginCore.SearchWindow.InputDataAnalyzer;
using Serilog;

namespace Kitopia.Desktop.Features.Search.InputProcessing;

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
                DisposeAction = e =>
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
