using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using PluginCore.Onnx;

namespace OnnxRuntime.OpenVino;


public class GPUOVInferenceSession : IInferenceSession
{
    public string Device => "GPU(OpenVino)";
    private InferenceSession? _inferenceSession;
    public void InitSession(string modelPath)
    {
        var sessionOptions = new SessionOptions();
        sessionOptions.AppendExecutionProvider_OpenVINO("GPU");
        _inferenceSession= new InferenceSession(modelPath, sessionOptions);
    }

    public void InitSession(byte[] modelData)
    {
        var sessionOptions = new SessionOptions();
        sessionOptions.AppendExecutionProvider_OpenVINO("GPU");
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
    
    public void Dispose()
    {
        _inferenceSession?.Dispose();
    }
}