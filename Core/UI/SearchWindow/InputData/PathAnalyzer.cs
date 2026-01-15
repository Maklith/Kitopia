using System.Collections.Concurrent;
using Core.Services;
using Core.Services.Interfaces;
using Core.ViewModel;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.SearchWindow.InputDataAnalyzer;

namespace Core.UI.SearchWindow.InputData;

public class PathAnalyzer : IInputDataAnalyzer
{
    public InputDataAnalyzeTimeFlags AnalyzeTimeFlags => InputDataAnalyzeTimeFlags.InputEmpty |
                                                          InputDataAnalyzeTimeFlags.WindowShow |
                                                          InputDataAnalyzeTimeFlags.InputChanged;

    public IEnumerable<SearchViewItem> AnalyzeInputData(
        IEnumerable<PluginCore.SearchWindow.InputData.InputData> inputDatas)
    {
        foreach (var inputData in inputDatas)
            if (inputData.InputType == InputType.目录)
            {
                ConcurrentDictionary<string, SearchViewItem> a = new();
                ServiceManager.Services.GetService<IAppToolService>()!.AppSolverA(a, (string)inputData.Data);
                var directoryInfo = new DirectoryInfo((string)inputData.Data);
                foreach (var (key, value) in a)
                {
                    value.ItemDisplayName = $"打开文件夹: {directoryInfo.Name} ?";


                    yield return value;
                    //GetIconInItemsAsync(value);
                }
            }
            else if (inputData.InputType == InputType.文件)
            {
                ConcurrentDictionary<string, SearchViewItem> a = new();
                ServiceManager.Services.GetService<IAppToolService>()!.AppSolverA(a, (string)inputData.Data);
                var fileInfo = new FileInfo((string)inputData.Data);
                foreach (var (key, value) in a)
                {
                    value.ItemDisplayName = $"打开文件: {fileInfo.Name} ?";

                    yield return value;
                    //GetIconInItemsAsync(value);
                }
            }

        yield break;
    }
}