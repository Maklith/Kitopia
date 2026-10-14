using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace KitopiaBenchmark;

/// <summary>
/// Measures native retention for the fixed-shape Chinese-CLIP image encoder.
/// </summary>
internal static class ClipNativeMemoryProbe
{
    private static readonly int[] Batches = [1, 2, 4, 8];

    public static void Run(string[] args)
    {
        var disableArena = args.Any(arg => string.Equals(arg, "--no-arena", StringComparison.OrdinalIgnoreCase));
        var modelPath = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal))
                        ?? Path.Combine(AppContext.BaseDirectory, "ChineseClip", "chinese-clip-rn50.img.int8.onnx");
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("The Chinese-CLIP image model was not found.", modelPath);
        }

        Console.WriteLine($"CPU arena: {(disableArena ? "disabled" : "enabled")}");
        Console.WriteLine($"Model: {modelPath}");
        PrintMemory("before session");
        RunModel(modelPath, disableArena);
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        PrintMemory("after session disposal");
    }

    private static void RunModel(string modelPath, bool disableArena)
    {
        using var options = new SessionOptions { EnableCpuMemArena = !disableArena };
        using var session = new InferenceSession(modelPath, options);
        var inputName = session.InputMetadata.Keys.First();
        Console.WriteLine($"Input: {inputName}");
        PrintMemory("after session creation");

        foreach (var batch in Batches)
        {
            var elementCount = checked(batch * 3 * 224 * 224);
            var input = new float[elementCount];
            var shape = new[] { batch, 3, 224, 224 };
            var values = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(inputName, new DenseTensor<float>(input, shape))
            };
            var stopwatch = Stopwatch.StartNew();
            using (var outputs = session.Run(values))
            {
                GC.KeepAlive(outputs);
            }

            stopwatch.Stop();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            PrintMemory($"after batch {batch} ({stopwatch.ElapsedMilliseconds} ms)");
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
