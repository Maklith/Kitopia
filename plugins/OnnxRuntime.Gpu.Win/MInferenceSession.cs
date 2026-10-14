using System.Threading;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using PluginCore.Onnx;

namespace OnnxRuntime.Gpu.Win;


public class MInferenceSession : IInferenceSession
{
    static MInferenceSession() => Shared.RuntimeAvailabilityProbe.ConfigureNativeLibraryResolution();

    // Preload once before ORT initializes CUDA. Keep handles alive for its native providers.
    private static readonly Lazy<nint[]> _cudnnLibraries = new(() => LoadCudnnLibraries(GetCudnnDirectories()));

    public string Device => "GPU(CUDA)";
    public bool? CheckAvailability()
    {
        _ = _cudnnLibraries.Value;
        using var options = new SessionOptions();
        options.AppendExecutionProvider_CUDA();
        return Shared.RuntimeAvailabilityProbe.Check(options, requireExecutionProvider: true, requireCudnn: true);
    }

    private InferenceSession? _inferenceSession;
    private IReadOnlyList<string> _inputNames = [];
    private IReadOnlyList<int[]> _outputShape = [];
    public void InitSession(string modelPath) => InitSession(modelPath, useCpuMemoryArena: true);

    public void InitSession(string modelPath, bool useCpuMemoryArena) => InitSession(modelPath, useCpuMemoryArena, 0);

    public void InitSession(string modelPath, bool useCpuMemoryArena, int intraOpNumThreads) =>
        InitSession(modelPath, useCpuMemoryArena, intraOpNumThreads, gpuMemoryLimitBytes: 0);

    public void InitSession(string modelPath, bool useCpuMemoryArena, int intraOpNumThreads, long gpuMemoryLimitBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(gpuMemoryLimitBytes);
        _ = _cudnnLibraries.Value;
        using var sessionOptions = new SessionOptions
        {
            EnableCpuMemArena = useCpuMemoryArena, EnableMemoryPattern = false,
            IntraOpNumThreads = intraOpNumThreads, InterOpNumThreads = 1
        };
        sessionOptions.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
        sessionOptions.AddSessionConfigEntry("session.inter_op.allow_spinning", "0");
        using var cudaOptions = new OrtCUDAProviderOptions();
        var providerOptions = new Dictionary<string, string> { ["arena_extend_strategy"] = "kSameAsRequested" };
        if (gpuMemoryLimitBytes > 0)
            providerOptions["gpu_mem_limit"] = gpuMemoryLimitBytes.ToString(System.Globalization.CultureInfo.InvariantCulture);
        cudaOptions.UpdateOptions(providerOptions);
        sessionOptions.AppendExecutionProvider_CUDA(cudaOptions);
        InferenceSession session;
        try
        {
            session = new InferenceSession(modelPath, sessionOptions);
        }
        catch (OnnxRuntimeException exception) when (IsAllocationFailure(exception))
        {
            throw new OutOfMemoryException("CUDA could not load the model within the available GPU memory or arena limit.", exception);
        }
        _inferenceSession?.Dispose();
        _inferenceSession = session;
        CacheMetadata(session);
    }

    public void InitSession(byte[] modelData)
    {
        _ = _cudnnLibraries.Value;
        using var sessionOptions = new SessionOptions();
        sessionOptions.AppendExecutionProvider_CUDA();
        var session = new InferenceSession(modelData, sessionOptions);
        _inferenceSession?.Dispose();
        _inferenceSession = session;
        CacheMetadata(session);
    }

    public IReadOnlyList<string> InputNames => _inputNames;

    public IReadOnlyList<int[]> OutputShape => _outputShape;
    public Memory<float> Infer(List<(string, Memory<int>, Memory<float>)> inputs)
    {
        var namedOnnxValues = inputs.Select(e=>NamedOnnxValue.CreateFromTensor(e.Item1, new DenseTensor<float>( e.Item3, e.Item2.Span))).ToList();
        using var outputs = _inferenceSession?.Run(namedOnnxValues)
                            ?? throw new InvalidOperationException("The inference session has not been initialized.");
        return outputs[0].AsTensor<float>().ToArray();
    }

    public Memory<float> Infer(List<(string, Memory<int>, Memory<float>)> inputs, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = _inferenceSession ?? throw new InvalidOperationException("The inference session has not been initialized.");
        var namedOnnxValues = inputs.Select(e => NamedOnnxValue.CreateFromTensor(e.Item1,
            new DenseTensor<float>(e.Item3, e.Item2.Span))).ToList();
        using var runOptions = new RunOptions();
        using var registration = cancellationToken.Register(() => runOptions.Terminate = true);
        try
        {
            using var outputs = session.Run(namedOnnxValues, session.OutputMetadata.Keys.ToArray(), runOptions);
            cancellationToken.ThrowIfCancellationRequested();
            return outputs[0].AsTensor<float>().ToArray();
        }
        catch (OnnxRuntimeException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    public Memory<float> InferInt64(List<(string, Memory<int>, Memory<long>)> inputs)
    {
        var namedOnnxValues = inputs
            .Select(e => NamedOnnxValue.CreateFromTensor(e.Item1, new DenseTensor<long>(e.Item3, e.Item2.Span)))
            .ToList();
        using var outputs = _inferenceSession?.Run(namedOnnxValues)
                            ?? throw new InvalidOperationException("The inference session has not been initialized.");
        return outputs[0].AsTensor<float>().ToArray();
    }

    public Memory<float> InferInt64(
        List<(string, Memory<int>, Memory<long>)> inputs,
        string outputName)
    {
        var namedOnnxValues = inputs
            .Select(e => NamedOnnxValue.CreateFromTensor(e.Item1, new DenseTensor<long>(e.Item3, e.Item2.Span)))
            .ToList();
        using var outputs = _inferenceSession?.Run(namedOnnxValues, [outputName])
                            ?? throw new InvalidOperationException("The inference session has not been initialized.");
        return outputs[0].AsTensor<float>().ToArray();
    }

    public Memory<float> InferInt64(
        List<(string, Memory<int>, Memory<long>)> inputs,
        string outputName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = _inferenceSession ?? throw new InvalidOperationException("The inference session has not been initialized.");
        var namedOnnxValues = inputs.Select(e => NamedOnnxValue.CreateFromTensor(e.Item1,
            new DenseTensor<long>(e.Item3, e.Item2.Span))).ToList();
        using var runOptions = new RunOptions();
        using var registration = cancellationToken.Register(() => runOptions.Terminate = true);
        try
        {
            using var outputs = session.Run(namedOnnxValues, [outputName], runOptions);
            cancellationToken.ThrowIfCancellationRequested();
            return outputs[0].AsTensor<float>().ToArray();
        }
        catch (OnnxRuntimeException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }
    
    public Memory<float> Infer(
        List<(string, Memory<int>, Memory<long>)> int64Inputs,
        List<(string, Memory<int>, Memory<float>)> floatInputs,
        string outputName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = _inferenceSession ?? throw new InvalidOperationException("The inference session has not been initialized.");
        var inputs = new List<NamedOnnxValue>(int64Inputs.Count + floatInputs.Count);
        foreach (var (name, shape, data) in int64Inputs)
            inputs.Add(NamedOnnxValue.CreateFromTensor(name, new DenseTensor<long>(data, shape.Span)));
        foreach (var (name, shape, data) in floatInputs)
            inputs.Add(NamedOnnxValue.CreateFromTensor(name, new DenseTensor<float>(data, shape.Span)));
        using var runOptions = new RunOptions();
        using var registration = cancellationToken.Register(() => runOptions.Terminate = true);
        try
        {
            using var outputs = session.Run(inputs, [outputName], runOptions);
            cancellationToken.ThrowIfCancellationRequested();
            return outputs[0].AsTensor<float>().ToArray();
        }
        catch (OnnxRuntimeException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (OnnxRuntimeException exception) when (IsAllocationFailure(exception))
        {
            throw new OutOfMemoryException("CUDA could not allocate inference memory within the available GPU memory or arena limit.", exception);
        }
    }


    public void Dispose()
    {
        _inferenceSession?.Dispose();
        _inferenceSession = null;
        _inputNames = [];
        _outputShape = [];
    }

    private void CacheMetadata(InferenceSession session)
    {
        _inputNames = session.InputMetadata.Keys.ToArray();
        _outputShape = session.OutputMetadata.Select(entry => entry.Value.Dimensions).ToArray();
    }

    // ORT reports allocator failures as FAIL, the same code used by unrelated execution errors.
    private static bool IsAllocationFailure(OnnxRuntimeException exception) =>
        (exception.Message.Contains("BFCArena::AllocateRawInternal", StringComparison.Ordinal)
         && (exception.Message.Contains("Available memory of", StringComparison.Ordinal)
             || exception.Message.Contains("Failed to allocate memory for requested buffer of size", StringComparison.Ordinal)))
        || exception.Message.Contains("CUDA failure 2: out of memory", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> GetCudnnDirectories()
    {
        var pluginDirectory = Path.GetDirectoryName(typeof(MInferenceSession).Assembly.Location)!;
        yield return pluginDirectory;
        yield return Path.Combine(pluginDirectory, "runtimes", "win-x64", "native");
        yield return AppContext.BaseDirectory;
        if (Environment.GetEnvironmentVariable("CUDNN_PATH") is { Length: > 0 } configured)
        {
            yield return configured;
            var bin = Path.Combine(configured, "bin");
            yield return bin;
            if (Directory.Exists(bin))
                foreach (var directory in Directory.EnumerateDirectories(bin, "13.*"))
                    yield return directory;
        }
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA", "CUDNN");
        if (Directory.Exists(root))
        {
            foreach (var version in Directory.EnumerateDirectories(root, "v9.*")
                         .OrderByDescending(path => Version.TryParse(Path.GetFileName(path).AsSpan(1), out var parsed) ? parsed : new Version()))
            {
                var bin = Path.Combine(version, "bin");
                if (!Directory.Exists(bin)) continue;
                foreach (var directory in Directory.EnumerateDirectories(bin, "13.*")
                             .OrderByDescending(path => Version.TryParse(Path.GetFileName(path), out var parsed) ? parsed : new Version()))
                    yield return directory;
            }
        }
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return directory.Trim('"');
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nuint GetCudnnCudaVersion();

    private static nint[] LoadCudnnLibraries(IEnumerable<string> directories)
    {
        foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.Combine(directory, "cudnn64_9.dll");
            if (!File.Exists(path)) continue;
            var handles = new List<nint>();
            try
            {
                // cuDNN 9 loads components lazily; preload them by absolute path as ORT recommends.
                foreach (var name in new[]
                         {
                             "cudnn_engines_runtime_compiled64_9.dll", "cudnn_engines_precompiled64_9.dll",
                             "cudnn_heuristic64_9.dll", "cudnn_ops64_9.dll", "cudnn_adv64_9.dll",
                             "cudnn_graph64_9.dll", "cudnn_cnn64_9.dll", "cudnn_engines_tensor_ir64_9.dll"
                         })
                {
                    var component = Path.Combine(directory, name);
                    if (name == "cudnn_engines_tensor_ir64_9.dll" && !File.Exists(component)) continue;
                    handles.Add(NativeLibrary.Load(component));
                }
                var handle = NativeLibrary.Load(path);
                handles.Add(handle);
                var cudaVersion = Marshal.GetDelegateForFunctionPointer<GetCudnnCudaVersion>(
                    NativeLibrary.GetExport(handle, "cudnnGetCudartVersion"))();
                if (cudaVersion < 13000 || cudaVersion >= 14000)
                    throw new NotSupportedException(
                        $"'{path}' was built for CUDA {cudaVersion / 1000}, but this plugin requires cuDNN 9 for CUDA 13. " +
                        "Correct CUDNN_PATH or PATH, then restart Kitopia.");
                return handles.ToArray();
            }
            catch (Exception exception)
            {
                for (var index = handles.Count - 1; index >= 0; index--) NativeLibrary.Free(handles[index]);
                throw new DllNotFoundException($"Failed to preload cuDNN 9 for CUDA 13 from '{directory}': {exception.Message}", exception);
            }
        }
        throw new DllNotFoundException(
            "cuDNN 9 for CUDA 13 was not found (cudnn64_9.dll). Install the CUDA 13 build of cuDNN 9, " +
            "or add its DLL directory to CUDNN_PATH or PATH, then restart Kitopia.");
    }
}
