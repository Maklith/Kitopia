using BenchmarkDotNet.Running;

namespace KitopiaBenchmark;

internal class Program
{
    private static void Main(string[] args)
    {
        if (args.FirstOrDefault()?.Equals("ocr", StringComparison.OrdinalIgnoreCase) == true)
        {
            BenchmarkSwitcher.FromTypes([typeof(OcrPreprocessingBenchmark)]).Run(args.Skip(1).ToArray());
            return;
        }

        if (args.FirstOrDefault()?.Equals("ocr-dnn", StringComparison.OrdinalIgnoreCase) == true)
        {
            BenchmarkSwitcher.FromTypes([typeof(OcrDnnPreprocessingBenchmark)]).Run(args.Skip(1).ToArray());
            return;
        }

        if (args.FirstOrDefault()?.Equals("search-index-memory", StringComparison.OrdinalIgnoreCase) == true)
        {
            BenchmarkSwitcher.FromTypes([typeof(OldVsNewMemory)]).Run(args.Skip(1).ToArray());
            return;
        }

        if (args.FirstOrDefault()?.Equals("ocr-native-memory", StringComparison.OrdinalIgnoreCase) == true)
        {
            OcrNativeMemoryProbe.Run(args.Skip(1).ToArray());
            return;
        }

        if (args.FirstOrDefault()?.Equals("bge-native-memory", StringComparison.OrdinalIgnoreCase) == true)
        {
            BgeNativeMemoryProbe.Run(args.Skip(1).ToArray());
            return;
        }

        if (args.FirstOrDefault()?.Equals("clip-native-memory", StringComparison.OrdinalIgnoreCase) == true)
        {
            ClipNativeMemoryProbe.Run(args.Skip(1).ToArray());
            return;
        }

        BenchmarkRunner.Run<DocumentTextExtractorBenchmark>();
    }
}
