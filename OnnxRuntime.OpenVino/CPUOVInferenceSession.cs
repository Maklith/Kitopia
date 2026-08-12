using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using PluginCore.Onnx;

namespace OnnxRuntime.OpenVino;


public class CPUOVInferenceSession : IInferenceSession
{
    public string Device => "CPU(OpenVino)";
    private InferenceSession? _inferenceSession;
    public void InitSession(string modelPath)
    {
        var sessionOptions = new SessionOptions();
        sessionOptions.AppendExecutionProvider_OpenVINO("CPU");
        _inferenceSession= new InferenceSession(modelPath, sessionOptions);
    }

    public void InitSession(byte[] modelData)
    {
        var sessionOptions = new SessionOptions();
        sessionOptions.AppendExecutionProvider_OpenVINO("CPU");
        _inferenceSession= new InferenceSession(modelData, sessionOptions);
    }

    public IReadOnlyList<string> InputNames => _inferenceSession?.InputMetadata.Keys.ToList();

    public IReadOnlyList<int[]> OutputShape => _inferenceSession?.OutputMetadata.Select(e=>e.Value.Dimensions).ToList();
    public Memory<float> Infer(List<(string, Memory<int>, Memory<float>)> inputs)
    {
        var namedOnnxValues = inputs.Select(e=>NamedOnnxValue.CreateFromTensor(e.Item1, new DenseTensor<float>( e.Item3, e.Item2.Span))).ToList();
        var asTensor = _inferenceSession?.Run(namedOnnxValues)[0].AsTensor<float>();
        
        if (asTensor is DenseTensor<float> floats)
        {
            return floats.Buffer;
        }
        return null;
    }

    public Memory<float> InferInt64(List<(string, Memory<int>, Memory<long>)> inputs)
    {
        var namedOnnxValues = inputs
            .Select(e => NamedOnnxValue.CreateFromTensor(e.Item1, new DenseTensor<long>(e.Item3, e.Item2.Span)))
            .ToList();
        var asTensor = _inferenceSession?.Run(namedOnnxValues)[0].AsTensor<float>();

        return asTensor is DenseTensor<float> floats ? floats.Buffer : asTensor?.ToArray() ?? [];
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
        var asTensor = outputs[0].AsTensor<float>();

        return asTensor is DenseTensor<float> floats ? floats.ToArray() : asTensor?.ToArray() ?? [];
    }
    
    public void Dispose()
    {
        _inferenceSession?.Dispose();
    }
}
