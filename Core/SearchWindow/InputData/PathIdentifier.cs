using System.Collections.Concurrent;
using Core.SDKs.Services;
using Core.ViewModel;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using Serilog;

namespace Core.SearchWindow.InputData;

public class PathIdentifier : IInputDataIdentifier
{
    private static ILogger Log =   LogManager.Logger.ForContext<SearchWindowViewModel>();
    public IEnumerable<Core.ViewModel.InputData> IdentifyInputData(string? text)
    {
        foreach (var inputData in PathChecker(text)) yield return inputData;
        
        var data = ServiceManager.Services.GetService<IClipboardService>()!
            .HasText();
        if (data)
        {
            var text2 = ServiceManager.Services.GetService<IClipboardService>()!
                .GetText();
            foreach (var inputData in PathChecker(text2)) yield return inputData;
        }
    }

    private static IEnumerable<ViewModel.InputData> PathChecker(string? text)
    {
        if (text.StartsWith("\"")) text = text.Remove(0,1);
        if (text.EndsWith("\"")) text = text.Remove(text.Length - 1, 1);
        if (Path.HasExtension(text) && File.Exists(text))
        {
            var fileInfo = new FileInfo(text);
            Log.Debug($"检测路径{fileInfo.FullName}");
            yield return new ViewModel.InputData()
            {
                InputType = InputType.文件,
                Data = fileInfo.FullName
            };
            if (fileInfo.Directory?.FullName != null)
                yield return new ViewModel.InputData()
                {
                    InputType = InputType.目录,
                    Data = fileInfo.Directory?.FullName
                };
        }
        else if (Directory.Exists(text))
        {
            var directoryInfo = new DirectoryInfo(text);
            Log.Debug($"检测路径{directoryInfo.FullName}");
            yield return new ViewModel.InputData()
            {
                InputType = InputType.目录,
                Data = directoryInfo.FullName
            };
        }
        yield break;
    }
}