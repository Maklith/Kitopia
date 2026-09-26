using System.Threading;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using PluginCore.Onnx;

namespace OnnxRuntime.OpenVino;


public class NPUOVInferenceSession : IInferenceSession
{
    public string Device => "NPU(OpenVino)";
    private InferenceSession? _inferenceSession;
    private IReadOnlyList<string> _inputNames = [];
    private IReadOnlyList<int[]> _outputShape = [];
    public void InitSession(string modelPath) => InitSession(modelPath, useCpuMemoryArena: true);

    public void InitSession(string modelPath, bool useCpuMemoryArena)
    {
        using var sessionOptions = new SessionOptions { EnableCpuMemArena = useCpuMemoryArena };
        sessionOptions.AppendExecutionProvider_OpenVINO("NPU");
        var session = new InferenceSession(modelPath, sessionOptions);
        _inferenceSession?.Dispose();
        _inferenceSession = session;
        CacheMetadata(session);
    }

    public void InitSession(byte[] modelData)
    {
        using var sessionOptions = new SessionOptions();
        sessionOptions.AppendExecutionProvider_OpenVINO("NPU");
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
}
