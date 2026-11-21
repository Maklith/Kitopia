using System.Buffers;
using PluginCore.SearchWindow.InputData;
using PluginCore.SearchWindow.InputDataAnalyzer;

namespace Kitopia.Desktop.Features.Search.InputProcessing;

public class MathIdentifier : IInputDataIdentifier
{
    private static readonly SearchValues<char> Operators = SearchValues.Create("*+-/^");

    public IEnumerable<PluginCore.SearchWindow.InputData.InputData> IdentifyInputData(
        InputDataAnalyzeTimeFlags analyzeTimeFlags, string? s)
    {
        if (string.IsNullOrEmpty(s) || s.AsSpan().IndexOfAny(Operators) < 0)
        {
            yield break;
        }

        foreach (var ch in s)
        {
            if (ch is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '\u4e00' and <= '\u9fa5'))
            {
                yield break;
            }
        }

        yield return new PluginCore.SearchWindow.InputData.InputData
        {
            InputType = InputType.数学表达式,
            Data = s
        };
    }
}
