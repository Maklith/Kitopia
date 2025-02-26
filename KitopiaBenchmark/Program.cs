using BenchmarkDotNet.Running;

namespace KitopiaBenchmark;

internal class Program
{
    private static void Main(string[] args)
    {
        BenchmarkRunner.Run<ScreenCapture>();
    }
}