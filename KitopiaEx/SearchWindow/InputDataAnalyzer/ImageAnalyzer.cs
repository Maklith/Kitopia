// Author: liaom
// SolutionName: Kitopia
// ProjectName: KitopiaEx
// FileName:ImageAnalyzer.cs
// Date: 2026/01/05 16:01
// FileEffect:

using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using PluginCore;
using PluginCore.SearchWindow.InputData;
using PluginCore.SearchWindow.InputDataAnalyzer;

namespace KitopiaEx.SearchWindow.InputDataAnalyzer;

public class ImageAnalyzer : IInputDataAnalyzer
{
    public InputDataAnalyzeTimeFlags AnalyzeTimeFlags => InputDataAnalyzeTimeFlags.InputEmpty | InputDataAnalyzeTimeFlags.WindowShow;

    public IEnumerable<SearchViewItem> AnalyzeInputData(IEnumerable<InputData> inputDatas)
    {
        foreach (var inputData in inputDatas)
            if (inputData.InputType == InputType.图像)
            {
                var image = inputData.Data as Mat;
                if (image == null)
                    yield break;
                //Pin
                yield return new SearchViewItem
                {
                    ItemDisplayName = "置顶图片",
                    FileType = FileType.自定义,
                    IconSymbol = 0xf602,
                    Icon = null,
                    IsVisible = true,
                    ShowAsMiniApp = true,
                    Action = (item, s) =>
                    {
                        KitopiaEx.ServiceProvider.GetService<ScreenCaptureExs.ImagePin>()!.PinBase(image);
                    }
                };
               //Ocr
               yield return new SearchViewItem
               {
                   ItemDisplayName = "文字提取",
                   FileType = FileType.自定义,
                   IconSymbol = 0xEA72,
                   Icon = null,
                   IsVisible = true,
                   ShowAsMiniApp = true,
                   Action = (item, s) =>
                   {
                       var service = KitopiaEx.ServiceProvider.GetService<global::KitopiaEx.CustomScenarioMethods.Ocr>();
                       var ocrResults = service!.OcrImgBase(image, CancellationToken.None);
                       service.OcrResultShowBase(image, ocrResults, CancellationToken.None);
                   }
               };
               
            }
        yield break;
    }
}