using System.Collections.Generic;
using Core.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.SearchWindow.InputData;
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
                SearchIndex a = new();
                ServiceManager.Services.GetService<IAppToolService>()!.IndexItem(a, (string)inputData.Data);
                var directoryInfo = new DirectoryInfo((string)inputData.Data);
                foreach (var (key, entry) in a.GetEntriesSnapshot())
                {
                    var item = entry.ToSearchViewItem();
                    item.ItemDisplayName = $"打开文件夹: {directoryInfo.Name} ?";
                    yield return item;
                }
            }
            else if (inputData.InputType == InputType.文件)
            {
                SearchIndex a = new();
                ServiceManager.Services.GetService<IAppToolService>()!.IndexItem(a, (string)inputData.Data);
                var fileInfo = new FileInfo((string)inputData.Data);
                foreach (var (key, entry) in a.GetEntriesSnapshot())
                {
                    var item = entry.ToSearchViewItem();
                    item.ItemDisplayName = $"打开文件: {fileInfo.Name} ?";
                    yield return item;
                }
            }
    }
}
