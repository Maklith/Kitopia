using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace KitopiaBenchmark;

/// <summary>
/// Measures native ONNX Runtime retention for the dynamic PaddleOCR detector input.
/// Run each arena mode in a separate process because an allocator arena intentionally
/// keeps its high-water allocations until its session is disposed.
/// </summary>
internal static class OcrNativeMemoryProbe
{
    private static readonly (int Height, int Width)[] Shapes =
    [
        (768, 1280),
        (1152, 1920),
        (1536, 2048),
        (2048, 2048)
    ];

    public static void Run(string[] args)
    {
        var disableArena = args.Any(arg => string.Equals(arg, "--no-arena", StringComparison.OrdinalIgnoreCase));
        var modelPath = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal))
                        ?? Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "Kitopia", "Ocr", "ppocrv6_tiny_det.onnx");
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("PaddleOCR detector model was not found.", modelPath);
        }

        Console.WriteLine($"CPU arena: {(disableArena ? "disabled" : "enabled")}");
        Console.WriteLine($"Model: {modelPath}");
        PrintMemory("before session");
        RunDetector(modelPath, disableArena);
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        PrintMemory("after session disposal");
    }

    private static void RunDetector(string modelPath, bool disableArena)
    {
        using var options = new SessionOptions { EnableCpuMemArena = !disableArena };
        using var session = new InferenceSession(modelPath, options);
        var inputName = session.InputMetadata.Keys.Single();
        PrintMemory("after session creation");

        foreach (var (height, width) in Shapes)
        {
            var input = new float[checked(3 * height * width)];
            var stopwatch = Stopwatch.StartNew();
            using var outputs = session.Run(
            [NamedOnnxValue.CreateFromTensor(inputName, new DenseTensor<float>(input, [1, 3, height, width]))]);
            stopwatch.Stop();

            GC.KeepAlive(outputs[0].AsTensor<float>());
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            PrintMemory($"after {width}x{height} ({stopwatch.ElapsedMilliseconds} ms)");
        }
    }

    private static void PrintMemory(string stage)
    {
        using var process = Process.GetCurrentProcess();
        Console.WriteLine(
            $"{stage}: private={process.PrivateMemorySize64 / 1024d / 1024d:F1} MB, " +
            $"working-set={process.WorkingSet64 / 1024d / 1024d:F1} MB, " +
            $"managed={GC.GetTotalMemory(forceFullCollection: false) / 1024d / 1024d:F1} MB");
    }
}
