using System.Text.RegularExpressions;
using Core.ViewModel;
using PluginCore;
using PluginCore.SearchWindow.InputDataAnalyzer;
using Math = Core.SDKs.Tools.Math;

namespace Core.SearchWindow.InputData;

public class MathAnalyzer : IInputDataAnalyzer
{
    public IInputDataAnalyzeTimeFlags AnalyzeTimeFlags => IInputDataAnalyzeTimeFlags.搜索时;
    public IEnumerable<SearchViewItem> AnalyzeInputData(IEnumerable<PluginCore.SearchWindow.InputData.InputData> inputDatas)
    {
        foreach (var inputData in inputDatas)
        {
            if (inputData.InputType== InputType.数学表达式)
            {
                SearchViewItem? item;
                var value = (string)inputData.Data;
                try
                {
                   
                    var e = Math.Evaluate(value);
                    item= (new SearchViewItem
                    {
                        ItemDisplayName = "=" + e,
                        FileType = FileType.数学运算,
                        OnlyKey = value,
                        Icon = null,
                        IconSymbol = 61547,
                        IsVisible = true
                    });
                }
                catch (Exception)
                {
                    item= (new SearchViewItem
                    {
                        ItemDisplayName = "错误的表达式",
                        FileType = FileType.数学运算,
                        OnlyKey = value,
                        Icon = null,
                        IconSymbol = 61547,
                        IsVisible = true
                    });
                }
                yield return item;
            }
        }
        yield break;
    }
}