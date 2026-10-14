using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.SearchWindow.InputData;
using PluginCore.SearchWindow.InputDataAnalyzer;

namespace Kitopia.Desktop.Features.Search.InputProcessing;

public partial class UrlIdentifier : IInputDataIdentifier
{
    public IEnumerable<PluginCore.SearchWindow.InputData.InputData> IdentifyInputData(
        InputDataAnalyzeTimeFlags analyzeTimeFlags, string? s)
    {
        foreach (var inputData in MatchAndReturnUrlData(s)) yield return inputData;
        if (analyzeTimeFlags.HasFlag(InputDataAnalyzeTimeFlags.WindowShow) ||
            analyzeTimeFlags.HasFlag(InputDataAnalyzeTimeFlags.InputEmpty))
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

    private static IEnumerable<PluginCore.SearchWindow.InputData.InputData> MatchAndReturnUrlData(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) yield break;

        if (UrlRegex().IsMatch(s))
        {
            if (Uri.TryCreate(s, UriKind.Absolute, out var uri))
                yield return new PluginCore.SearchWindow.InputData.InputData
                {
                    InputType = InputType.网址,
                    Data = uri.ToString()
                };
            yield break;
        }

        if (DomainRegex().IsMatch(s))
        {
            yield return new PluginCore.SearchWindow.InputData.InputData
            {
                InputType = InputType.网址,
                Data = s
            };
            yield break;
        }

        if (Uri.TryCreate(s, UriKind.Absolute, out var uri2) && IsWebScheme(uri2.Scheme))
            yield return new PluginCore.SearchWindow.InputData.InputData
            {
                InputType = InputType.网址,
                Data = uri2.ToString()
            };
    }

    private static bool IsWebScheme(string scheme)
    {
        return scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
               scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"^(?=^.{3,255}$)(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,}$")]
    private static partial Regex DomainRegex();

    [GeneratedRegex(@"^https?://\S+$")]
    private static partial Regex UrlRegex();
}
