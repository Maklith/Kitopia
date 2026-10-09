using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace OnnxRuntime.Shared;

internal static class RuntimeAvailabilityProbe
{
    public static void ConfigureNativeLibraryResolution()
    {
        var resolver = new AssemblyDependencyResolver(typeof(RuntimeAvailabilityProbe).Assembly.Location);
        try
        {
            // ORT's name-first Windows resolver can pick up an incompatible system32 DLL.
            NativeLibrary.SetDllImportResolver(typeof(InferenceSession).Assembly, (name, _, _) =>
            {
                var path = resolver.ResolveUnmanagedDllToPath(name);
                if (path is null && (name.Equals("onnxruntime", StringComparison.OrdinalIgnoreCase) ||
                                     name.Equals("onnxruntime.dll", StringComparison.OrdinalIgnoreCase)))
                {
                    var directory = Path.GetDirectoryName(typeof(RuntimeAvailabilityProbe).Assembly.Location)!;
                    var fileName = OperatingSystem.IsWindows() ? "onnxruntime.dll"
                        : OperatingSystem.IsMacOS() ? "libonnxruntime.dylib" : "libonnxruntime.so";
                    var localPath = Path.Combine(directory, fileName);
                    if (!File.Exists(localPath))
                        localPath = Path.Combine(directory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", fileName);
                    if (!File.Exists(localPath))
                        throw new DllNotFoundException($"The runtime plugin's {fileName} is missing. Reinstall the plugin.");
                    path = localPath;
                }
                return path is null ? IntPtr.Zero : NativeLibrary.Load(path);
            });
        }
        catch (InvalidOperationException)
        {
            // A host or another device in this plugin already installed the assembly's resolver.
        }
    }

    // Opset 13, static [1,16] MatMul with identity weights. Keep computation on the requested provider.
    private static readonly byte[] Model = Convert.FromBase64String(
        "CAg6+QgKIAoFaW5wdXQKB3dlaWdodHMSBm91dHB1dCIGTWF0TXVsEg1ydW50aW1lLXByb2JlKpIICBAIEBABIoAIAACAPwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIA/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAgD8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACAPwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIA/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAgD8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACAPwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIA/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAgD8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACAPwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIA/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAgD8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACAPwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIA/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAgD8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACAP0IHd2VpZ2h0c1oXCgVpbnB1dBIOCgwIARIICgIIAQoCCBBiGAoGb3V0cHV0Eg4KDAgBEggKAggBCgIIEEIECgAQDQ==");

    // CUDA: MatMul + BiasSoftmax exercises both cuBLAS and cuDNN without CPU fallback.
    private static readonly byte[] _cudnnModel = Convert.FromBase64String(
        "CAg6qAoKIAoFaW5wdXQKB3dlaWdodHMSBmxvZ2l0cyIGTWF0TXVsCloKBmxvZ2l0cwoEYmlhcxIGb3V0cHV0IgtCaWFzU29mdG1heCoLCgRheGlzGAGgAQIqGQoSaXNfaW5uZXJfYnJvYWRjYXN0GACgAQI6DWNvbS5taWNyb3NvZnQSEGN1ZGEtY3Vkbm4tcHJvYmUqkggIEAgQEAEigAgAAIA/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAgD8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACAPwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIA/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAgD8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACAPwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIA/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAgD8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACAPwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIA/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAgD8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACAPwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIA/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAgD8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACAPwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIA/Qgd3ZWlnaHRzKk4IAQgQEAEiQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABCBGJpYXNaFwoFaW5wdXQSDgoMCAESCAoCCAEKAggQYhgKBm91dHB1dBIOCgwIARIICgIIAQoCCBBCBAoAEA1CEQoNY29tLm1pY3Jvc29mdBAB");

    public static bool Check(SessionOptions options, bool requireExecutionProvider, bool requireCudnn = false)
    {
        options.IntraOpNumThreads = 1;
        options.InterOpNumThreads = 1;
        options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
        options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_DISABLE_ALL;
        if (requireExecutionProvider)
            options.AddSessionConfigEntry("session.disable_cpu_ep_fallback", "1");

        using var session = new InferenceSession(requireCudnn ? _cudnnModel : Model, options);
        var input = requireCudnn ? new float[16] : Enumerable.Range(1, 16).Select(value => (float)value).ToArray();
        using var outputs = session.Run([
            NamedOnnxValue.CreateFromTensor("input", new DenseTensor<float>(input, [1, 16]))
        ]);
        var output = outputs[0].AsTensor<float>();
        if (output.Length != input.Length || output.Where((value, index) =>
                !float.IsFinite(value) || Math.Abs(value - (requireCudnn ? 1f / 16 : input[index])) > 0.001f).Any())
            throw new InvalidOperationException("The runtime probe produced an invalid inference result.");
        return true;
    }
}
