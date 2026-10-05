// Author: liaom
// SolutionName: Kitopia
// ProjectName: KitopiaEx
// FileName:ImageAnalyzer.cs
// Date: 2026/01/05 16:01
// FileEffect:

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using KitopiaEx.ImageCompression;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using PluginCore;
using PluginCore.Localization;
using PluginCore.SearchWindow.InputData;
using PluginCore.SearchWindow.InputDataAnalyzer;

namespace KitopiaEx.SearchWindow.InputDataAnalyzer;

public class ImageAnalyzer : IInputDataAnalyzer
{
    public InputDataAnalyzeTimeFlags AnalyzeTimeFlags => InputDataAnalyzeTimeFlags.InputEmpty |
        InputDataAnalyzeTimeFlags.WindowShow | InputDataAnalyzeTimeFlags.InputChanged;

    public IEnumerable<SearchViewItem> AnalyzeInputData(IEnumerable<InputData> inputDatas)
    {
        var inputs = inputDatas as IReadOnlyCollection<InputData> ?? inputDatas.ToArray();
        var paths = inputs.Where(input => input.InputType == InputType.文件)
            .Select(input => input.Data).OfType<string>()
            .Where(path => ImageCompressor.IsSupported(path) && File.Exists(path))
            .Distinct(System.OperatingSystem.IsWindows() ? System.StringComparer.OrdinalIgnoreCase : System.StringComparer.Ordinal)
            .ToArray();
        if (paths.Length > 0)
            yield return new SearchViewItem
            {
                ItemDisplayName = Lang.Get("lang.kitopiaex.compression.title"),
                FileType = FileType.自定义,
                IconSymbol = 0xEA14,
                IsVisible = true,
                ShowAsMiniApp = true,
                Action = (_, _) => _ = ImageCompressionWindow.OpenFilesAsync(paths)
            };
        foreach (var inputData in inputs)
            if (inputData.InputType == InputType.图像)
            {
                var image = inputData.Data as Mat;
                if (image == null)
                    continue;
                if (paths.Length == 0)
                    yield return new SearchViewItem
                    {
                        ItemDisplayName = Lang.Get("lang.kitopiaex.compression.title"),
                        FileType = FileType.自定义,
                        IconSymbol = 0xEA14,
                        IsVisible = true,
                        ShowAsMiniApp = true,
                        Action = (_, _) =>
                        {
                            Cv2.ImEncode(".png", image, out var bytes);
                            // Search clears the input collection when an action is launched.
                            GC.KeepAlive(inputData);
                            _ = ImageCompressionWindow.OpenFilesAsync([], bytes);
                        }
                    };
                //Pin
                yield return new SearchViewItem
                {
                    ItemDisplayName = Lang.Get("lang.kitopiaex.pin_captured_image"),
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
                   ItemDisplayName = Lang.Get("lang.kitopiaex.extract_text"),
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
