using Core.Services;
using Core.ViewModel;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.SearchWindow.InputDataAnalyzer;
using Serilog;
using SearchWindowViewModel = Core.ViewModel.Windows.SearchWindowViewModel;

namespace Core.UI.SearchWindow.InputData;

public class PathIdentifier : IInputDataIdentifier
{
    private static ILogger Logger = LogManager.Logger.ForContext<SearchWindowViewModel>();

    public IEnumerable<PluginCore.SearchWindow.InputData.InputData> IdentifyInputData(
        InputDataAnalyzeTimeFlags analyzeTimeFlags, string? text)
    {
        foreach (var inputData in PathChecker(text)) yield return inputData;

        if (analyzeTimeFlags.HasFlag(InputDataAnalyzeTimeFlags.WindowShow) ||
            analyzeTimeFlags.HasFlag(InputDataAnalyzeTimeFlags.InputEmpty))
        {
            var data = ServiceManager.Services.GetService<IClipboardService>()!
                .HasText();
            if (data)
            {
                var text2 = ServiceManager.Services.GetService<IClipboardService>()!
                    .GetText();
                foreach (var inputData in PathChecker(text2)) yield return inputData;
            }
        }
    }

    private static IEnumerable<PluginCore.SearchWindow.InputData.InputData> PathChecker(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        if (text.StartsWith("\"")) text = text.Remove(0, 1);
        if (text.EndsWith("\"")) text = text.Remove(text.Length - 1, 1);
        if (Path.HasExtension(text) && File.Exists(text))
        {
            var fileInfo = new FileInfo(text);
            Logger.Debug($"检测路径{fileInfo.FullName}");
            yield return new PluginCore.SearchWindow.InputData.InputData
            {
                InputType = InputType.文件,
                Data = fileInfo.FullName
            };
            if (fileInfo.Directory?.FullName != null)
                yield return new PluginCore.SearchWindow.InputData.InputData
                {
                    InputType = InputType.目录,
                    Data = fileInfo.Directory?.FullName
                };
        }
        else if (Directory.Exists(text))
        {
            var directoryInfo = new DirectoryInfo(text);
            Logger.Debug($"检测路径{directoryInfo.FullName}");
            yield return new PluginCore.SearchWindow.InputData.InputData
            {
                InputType = InputType.目录,
                Data = directoryInfo.FullName
            };
        }

        yield break;
    }
}