using System.Text.RegularExpressions;
using Core.ViewModel;

namespace Core.SearchWindow.InputData;

public class MathIdentifier : IInputDataIdentifier
{
    public IEnumerable<ViewModel.InputData> IdentifyInputData(string? s)
    {
        var operators = new[] { '*', '+', '-', '/', '^' };
        var pattern = @"[\u4e00-\u9fa5a-zA-Z]+";
        if (s != null &&
            Regex.Match(s, pattern, RegexOptions.NonBacktracking)
                .Value == "" &&
            s.IndexOfAny(operators) > -1)
            yield return new ViewModel.InputData()
            {
                InputType = InputType.数学表达式,
                Data = s
            };
        yield break;
    }
}