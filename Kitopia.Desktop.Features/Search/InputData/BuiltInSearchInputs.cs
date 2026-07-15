using Kitopia.Desktop.Features.Services.Plugin;
using PluginCore;
using PluginCore.SearchWindow.InputData;
using PluginCore.SearchWindow.InputDataAnalyzer;

namespace Kitopia.Desktop.Features.Search.InputProcessing;

internal static class BuiltInSearchInputs
{
    private const string Owner = "Kitopia";
    private static readonly object SyncRoot = new();

    public static void EnsureRegistered()
    {
        lock (SyncRoot)
        {
            if (!PluginOverall.SearchWindowInputDataIdentifies.ContainsKey(Owner))
            {
                var customScenarioIdentifier = new CustomScenarioIdentifier();
                var urlIdentifier = new UrlIdentifier();
                var knowCommandIdentifier = new KnowCommandIdentifier();
                var mathIdentifier = new MathIdentifier();
                var imageIdentifier = new ImageIdentifier();
                var pathIdentifier = new PathIdentifier();

                PluginOverall.SearchWindowInputDataIdentifies[Owner] =
                [
                    (flags, value) => pathIdentifier.IdentifyInputData(flags, value),
                    (flags, value) => imageIdentifier.IdentifyInputData(flags, value),
                    (flags, value) => mathIdentifier.IdentifyInputData(flags, value),
                    (flags, value) => knowCommandIdentifier.IdentifyInputData(flags, value),
                    (flags, value) => urlIdentifier.IdentifyInputData(flags, value),
                    (flags, value) => customScenarioIdentifier.IdentifyInputData(flags, value)
                ];
            }

            if (!PluginOverall.SearchWindowInputDataAnalyzers.ContainsKey(Owner))
            {
                var pathAnalyzer = new PathAnalyzer();
                var imageAnalyzer = new ImageAnalyzer();
                var mathAnalyzer = new MathAnalyzer();
                var knowCommandAnalyzer = new KnowCommandAnalyzer();
                var urlAnalyzer = new UrlAnalyzer();
                var customScenarioAnalyzer = new CustomScenarioAnalyzer();

                PluginOverall.SearchWindowInputDataAnalyzers[Owner] =
                [
                    (() => pathAnalyzer.AnalyzeTimeFlags, input => pathAnalyzer.AnalyzeInputData(input)),
                    (() => imageAnalyzer.AnalyzeTimeFlags, input => imageAnalyzer.AnalyzeInputData(input)),
                    (() => mathAnalyzer.AnalyzeTimeFlags, input => mathAnalyzer.AnalyzeInputData(input)),
                    (() => knowCommandAnalyzer.AnalyzeTimeFlags, input => knowCommandAnalyzer.AnalyzeInputData(input)),
                    (() => urlAnalyzer.AnalyzeTimeFlags, input => urlAnalyzer.AnalyzeInputData(input)),
                    (() => customScenarioAnalyzer.AnalyzeTimeFlags,
                        input => customScenarioAnalyzer.AnalyzeInputData(input))
                ];
            }
        }
    }
}
