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

        BenchmarkRunner.Run<DocumentTextExtractorBenchmark>();
    }
}
