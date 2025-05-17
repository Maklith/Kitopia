using System.Collections.Generic;
using Avalonia.Threading;
using Core.ViewModel;
using PluginCore;
using PluginCore.SearchWindow.InputDataAnalyzer;

namespace KitopiaEx.Translate;

public class TranslateInputDataAnalyzer : IInputDataAnalyzer
{
    public IEnumerable<SearchViewItem> AnalyzeInputData(IEnumerable<InputData> inputDatas)
    {
        foreach (var inputData in inputDatas)
        {
            if (inputData.InputType is not InputType.文本)
                continue;
            var search = inputData.Data as string;
            if (search.StartsWith(Config.INSTANCE.TranslatePreString) || search.Length > Config.INSTANCE.TranslateMinCount)
            {
                yield return  new SearchViewItem()
                {
                    ItemDisplayName = "翻译",
                    FileType = FileType.自定义,
                    IconSymbol = 0xf834,
                    Action = (e,s) =>
                    {
                    
                        if (s.StartsWith(Config.INSTANCE.TranslatePreString))
                        {
                            s = search.Substring(Config.INSTANCE.TranslatePreString.Length);
                        }
                        Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            var translateWindowViewModel = new TranslateWindowViewModel()
                            {
                                SourceText = s
                            };
                            var translateWindow = new TranslateWindow()
                            {
                                DataContext = translateWindowViewModel
                            };
                            translateWindow.Show();
                        });

                    }
                };
            }
        }
        yield break;
    }
}