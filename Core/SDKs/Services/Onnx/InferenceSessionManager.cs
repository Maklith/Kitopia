using Core.SDKs.Services.Config;
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
        var target= ConfigManger.Config.OnnxTargetDevices.ContainsKey(onnxModelInfoWrapper.Model.SignName)
            ? ConfigManger.Config.OnnxTargetDevices[onnxModelInfoWrapper.Model.SignName]
            : "CPU";
        if (!PluginOverall.OnnxRuntimes.ContainsKey(target))
        {
            throw new Exception($"目标推理环境{target}不存在");
        }
        var onnxRuntime = PluginOverall.OnnxRuntimes[target].Invoke();
        var path = PluginManager.GetPluginByPlgStr(onnxModelInfoWrapper.PluginStr).Path;
        onnxRuntime.InitSession($"{path}{onnxModelInfoWrapper.Model.ModelPath}");
        return onnxRuntime;
    }
}