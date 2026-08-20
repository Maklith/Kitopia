using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace KitopiaBenchmark;

/// <summary>
/// Measures native ONNX Runtime retention for the dynamic BGE token inputs.
/// Run each arena mode in a separate process so an allocator high-water mark is
/// not shared by the two measurements.
/// </summary>
internal static class BgeNativeMemoryProbe
{
    private static readonly (int Batch, int Sequence)[] Shapes =
    [
        (1, 8),
        (1, 64),
        (1, 128),
        (8, 128),
        (32, 128),
        (32, 256),
        (1, 512)
    ];

    public static void Run(string[] args)
    {
        var disableArena = args.Any(arg => string.Equals(arg, "--no-arena", StringComparison.OrdinalIgnoreCase));
        var modelPath = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal))
                        ?? Path.Combine(AppContext.BaseDirectory, "BGE_Model", "quantized", "model_quantized.onnx");
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("The BGE model was not found.", modelPath);
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
        var inputNames = session.InputMetadata.Keys.ToArray();
        Console.WriteLine($"Inputs: {string.Join(", ", inputNames)}");
        PrintMemory("after session creation");

        foreach (var (batch, sequence) in Shapes)
        {
            var elementCount = checked(batch * sequence);
            var inputIds = new long[elementCount];
            var attentionMask = new long[elementCount];
            var tokenTypeIds = new long[elementCount];
            Array.Fill(attentionMask, 1L);
            var shape = new[] { batch, sequence };
            var inputs = new List<NamedOnnxValue>(inputNames.Length)
            {
                NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(inputIds, shape)),
                NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(attentionMask, shape)),
                NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(tokenTypeIds, shape))
            };

            var stopwatch = Stopwatch.StartNew();
            using (var outputs = session.Run(inputs))
            {
                GC.KeepAlive(outputs);
            }

            stopwatch.Stop();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            PrintMemory($"after {batch}x{sequence} ({stopwatch.ElapsedMilliseconds} ms)");
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
