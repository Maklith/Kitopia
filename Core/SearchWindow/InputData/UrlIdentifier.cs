using System.Text.RegularExpressions;
using Core.ViewModel;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.SearchWindow.InputDataAnalyzer;

namespace Core.SearchWindow.InputData;

public partial class UrlIdentifier: IInputDataIdentifier
{
    
    public IEnumerable<ViewModel.InputData> IdentifyInputData(IInputDataAnalyzeTimeFlags analyzeTimeFlags,string? s)
    {
        foreach (var inputData in MatchAndReturnUrlData(s)) yield return inputData;
        if (analyzeTimeFlags.HasFlag(IInputDataAnalyzeTimeFlags.仅有搜索内容打开时) ||
            analyzeTimeFlags.HasFlag(IInputDataAnalyzeTimeFlags.搜索前))
        {
            var data = ServiceManager.Services.GetService<IClipboardService>()!
                .HasText();
            if (data)
            {
                var text2 = ServiceManager.Services.GetService<IClipboardService>()!
                    .GetText();
                foreach (var inputData in MatchAndReturnUrlData(text2)) yield return inputData;
            }
        }
        
    }

    private static IEnumerable<ViewModel.InputData> MatchAndReturnUrlData(string? s)
    {
        if (s is null)
        {
            yield break;
        }
        if (DomainRegex().IsMatch(s) || UrlRegex().IsMatch(s))
        {
            yield return new ViewModel.InputData()
            {
                InputType = InputType.网址,
                Data = s
            };
        }

        if (Uri.TryCreate(s, UriKind.Absolute, out var uri))
        {
            yield return new ViewModel.InputData()
            {
                InputType = InputType.网址,
                Data = uri.ToString()
            };
        }
    }

    [GeneratedRegex("^(?=^.{3,255}$)[a-zA-Z0-9][-a-zA-Z0-9]{0,62}(\\.[a-zA-Z0-9][-a-zA-Z0-9]{0,62})+$")]
    private static partial Regex DomainRegex();
    
    [GeneratedRegex("^\\w+[^\\s]+(\\.[^\\s]+){1,}$")]
    private static partial Regex UrlRegex();
    
}