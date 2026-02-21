using System.Text.RegularExpressions;
using PluginCore.SearchWindow.InputData;
using PluginCore.SearchWindow.InputDataAnalyzer;

namespace Core.UI.SearchWindow.InputData;

public class MathIdentifier : IInputDataIdentifier
{
    public IEnumerable<PluginCore.SearchWindow.InputData.InputData> IdentifyInputData(
        InputDataAnalyzeTimeFlags analyzeTimeFlags, string? s)
    {
        var operators = new[] { '*', '+', '-', '/', '^' };
        var pattern = @"[\u4e00-\u9fa5a-zA-Z]+";
        if (s != null &&
            Regex.Match(s, pattern, RegexOptions.NonBacktracking)
                .Value == "" &&
            s.IndexOfAny(operators) > -1)
            yield return new PluginCore.SearchWindow.InputData.InputData
            {
                InputType = InputType.数学表达式,
                Data = s
            };
        yield break;
    }
}