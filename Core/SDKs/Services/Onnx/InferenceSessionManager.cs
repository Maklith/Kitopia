using Core.SDKs.Services.Plugin;
using PluginCore.Onnx;

namespace Core.SDKs.Services.Onnx;

public class InferenceSessionManager: IInferenceSessionManager
{
    public IInferenceSession GetSession(string modelSignName)
    {
        var onnxModelInfoWrapper = PluginOverall.OnnxModelInfos.SelectMany(e=>e.Value).FirstOrDefault(e=>e.Model.SignName==modelSignName);
        if (onnxModelInfoWrapper is null)
            return null;
        var onnxRuntime = PluginOverall.OnnxRuntimes[TargetDevice.CPU].Invoke();
        var path = PluginManager.GetPluginByPlgStr(onnxModelInfoWrapper.PluginStr).Path;
        onnxRuntime.InitSession($"{path}{onnxModelInfoWrapper.Model.ModelPath}");
        return onnxRuntime;
    }
}